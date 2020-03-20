// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using PlaceBulkResult = System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<BuildXL.Cache.ContentStore.Interfaces.Results.Indexed<BuildXL.Cache.ContentStore.Interfaces.Results.PlaceFileResult>>>;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// A read only content location based content session with an inner session for storage.
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class ReadOnlyDistributedContentSession<T> : ContentSessionBase, IHibernateContentSession, IConfigurablePin
        where T : PathBase
    {
        private enum Counters
        {
            GetLocationsSatisfiedFromLocal,
            GetLocationsSatisfiedFromRemote,
            PinUnverifiedCountSatisfied,
            ProactiveCopy_OutsideRingFromPreferredLocations
        }

        private readonly CounterCollection<Counters> _counters = new CounterCollection<Counters>();
        private RocksDbContentPlacementPredictionStore _predictionStore;
        private string _buildId = null;
        private ContentHash? _buildIdHash = null;
        private readonly ConcurrentBigSet<ContentHash> _pendingProactivePuts = new ConcurrentBigSet<ContentHash>();

        private static readonly string PredictionBlobNameFile = "blobName.txt";

        /// <summary>
        /// Caches pin operations
        /// </summary>
        private readonly PinCache _pinCache;

        // The method used for remote pins depends on which pin configuraiton is enabled.
        private readonly RemotePinAsync _remotePinner;

        private BackgroundTaskTracker _backgroundTaskTracker;

        /// <summary>
        /// The store that persists content locations to a persistent store.
        /// </summary>
        internal readonly IContentLocationStore ContentLocationStore;

        /// <summary>
        /// The machine location for the current cache.
        /// </summary>
        protected readonly MachineLocation LocalCacheRootMachineLocation;

        /// <summary>
        /// The content session that actually stores content.
        /// </summary>
        protected readonly IContentSession Inner;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedContentSession<T>));

        /// <nodoc />
        protected readonly DistributedContentCopier<T> DistributedCopier;

        private readonly IDistributedContentCopierHost _copierHost;

        /// <summary>
        /// Updates content tracker lazily or eagerly based on local age.
        /// </summary>
        private readonly ContentTrackerUpdater _contentTrackerUpdater;

        /// <summary>
        /// Settings for the session.
        /// </summary>
        protected readonly DistributedContentStoreSettings Settings;

        /// <summary>
        /// Trace only stops and errors to reduce the Kusto traffic.
        /// </summary>
        protected override bool TraceOperationStarted => false;

        /// <summary>
        /// Semaphore that limits the maximum number of concurrent put and place operations
        /// </summary>
        protected readonly SemaphoreSlim PutAndPlaceFileGate;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlyDistributedContentSession{T}"/> class.
        /// </summary>
        public ReadOnlyDistributedContentSession(
            string name,
            IContentSession inner,
            IContentLocationStore contentLocationStore,
            DistributedContentCopier<T> contentCopier,
            IDistributedContentCopierHost copierHost,
            MachineLocation localMachineLocation,
            PinCache pinCache = null,
            ContentTrackerUpdater contentTrackerUpdater = null,
            DistributedContentStoreSettings settings = default)
            : base(name)
        {
            Contract.Requires(name != null);
            Contract.Requires(inner != null);
            Contract.Requires(contentLocationStore != null);
            Contract.Requires(localMachineLocation.IsValid);

            Inner = inner;
            ContentLocationStore = contentLocationStore;
            LocalCacheRootMachineLocation = localMachineLocation;
            Settings = settings;
            _copierHost = copierHost;
            _pinCache = pinCache;

            // If no better pin configuration is supplied, fall back to the old remote pinning logic.
            if (pinCache != null)
            {
                _remotePinner = _pinCache.CreatePinner(PinFromMultiLevelContentLocationStore);
            }
            else
            {
                _remotePinner = PinFromMultiLevelContentLocationStore;
            }

            _contentTrackerUpdater = contentTrackerUpdater;
            DistributedCopier = contentCopier;
            PutAndPlaceFileGate = new SemaphoreSlim(settings.MaximumConcurrentPutAndPlaceFileOperations);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _backgroundTaskTracker = new BackgroundTaskTracker(Name, new Context(context));
            var canHibernate = Inner is IHibernateContentSession ? "can" : "cannot";
            Tracer.Debug(context, $"Session {Name} {canHibernate} hibernate");
            await Inner.StartupAsync(context).ThrowIfFailure();

            TryRegisterMachineWithBuildId(context);

            await InitializePredictionStoreAsync(context);

            return BoolResult.Success;
        }

        private void TryRegisterMachineWithBuildId(OperationContext context)
        {
            if (Constants.TryExtractBuildId(Name, out _buildId) && Guid.TryParse(_buildId, out var buildIdGuid))
            {
                // Generate a fake hash for the build and register a content entry in the location store to represent
                // machines in the build ring
                _buildIdHash = new ContentHash(HashType.MD5, buildIdGuid.ToByteArray());

                var arguments = $"Build={_buildId}, BuildIdHash={_buildIdHash.Value.ToShortString()}";

                context.PerformOperationAsync(Tracer, async () =>
                {
                    await ContentLocationStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(_buildIdHash.Value, _buildId.Length) }, context.Token, UrgencyHint.Nominal).ThrowIfFailure();

                    return BoolResult.Success;
                },
                extraStartMessage: arguments,
                extraEndMessage: r => arguments).FireAndForget(context);
            }
        }

        private Task InitializePredictionStoreAsync(OperationContext context)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var centralStorage = (ContentLocationStore as TransitioningContentLocationStore)?.LocalLocationStore?.CentralStorage;

                    if (Settings.ContentPlacementPredictionsBlob != null)
                    {
                        var checkpointDirectory = Path.Combine(LocalCacheRootMachineLocation.Path, "PlacementPredictions");
                        _predictionStore = new RocksDbContentPlacementPredictionStore(checkpointDirectory, clean: false);
                        await _predictionStore.StartupAsync(context).ThrowIfFailure();

                        var fileName = Path.Combine(checkpointDirectory, PredictionBlobNameFile);
                        if (!File.Exists(fileName) || File.ReadAllText(fileName) != Settings.ContentPlacementPredictionsBlob)
                        {
                            Directory.Delete(checkpointDirectory);

                            Directory.CreateDirectory(checkpointDirectory);

                            var zipFile = Path.Combine(checkpointDirectory, "snapshot.zip");
                            await centralStorage.TryGetFileAsync(context, Settings.ContentPlacementPredictionsBlob, new AbsolutePath(zipFile)).ThrowIfFailure();
                            _predictionStore.UncompressSnapshot(context, zipFile).ThrowIfFailure();
                        }
                    }

                    return BoolResult.Success;
                });
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var counterSet = new CounterSet();
            counterSet.Merge(GetCounters(), $"{Tracer.Name}.");

            if (_backgroundTaskTracker != null)
            {
                await _backgroundTaskTracker.Synchronize();
                await _backgroundTaskTracker.ShutdownAsync(context);
            }

            await Inner.ShutdownAsync(context).ThrowIfFailure();

            counterSet.LogOrderedNameValuePairs(s => Tracer.Debug(context, s));

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            Inner.Dispose();
            _backgroundTaskTracker?.Dispose();
        }

        /// <inheritdoc />
        protected override async Task<PinResult> PinCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // We could implement this method by calling into the bulk method with one hash in the list, but by implementing it separately
            // we can avoid the overhead of the paging and action-block logic there.

            // If pin better is off, continue the old behavior of re-directing to the bulk method.
            if (Settings.PinConfiguration == null)
            {
                var bulkResults = await PinAsync(operationContext, new[] { contentHash }, operationContext.Token, urgencyHint);
                return await bulkResults.SingleAwaitIndexed();
            }

            // First try a local pin.
            PinResult local = await Inner.PinAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
            if (local.Succeeded)
            {
                Tracer.Info(operationContext, $"Pin succeeded for {contentHash.ToShortString()}: local pin succeeded.");

                var contentHashInfo = new ContentHashWithSizeAndLastAccessTime(contentHash, local.ContentSize, local.LastAccessTime);
                await UpdateContentTrackerWithLocalHitsAsync(operationContext, new[] { contentHashInfo }, operationContext.Token, urgencyHint);
                return local;
            }

            // Next try the pin cache
            if (_pinCache?.TryPinFromCachedResult(contentHash).Succeeded == true)
            {
                Tracer.Info(operationContext, $"Pin succeeded for {contentHash.ToShortString()}: satisfied from cache.");

                return PinResult.Success;
            }

            // Then try to find remote copies from the distributed directory.
            foreach (var getBulkTask in ContentLocationStore.MultiLevelGetLocations(operationContext, new ContentHash[] { contentHash }, operationContext.Token, urgencyHint, subtractLocalResults: false))
            {
                var lookup = await getBulkTask;
                if (lookup.Succeeded)
                {
                    IReadOnlyList<ContentHashWithSizeAndLocations> records = lookup.ContentHashesInfo;
                    Contract.Assert(records != null);
                    Contract.Assert(records.Count == 1);
                    ContentHashWithSizeAndLocations record = records[0];
                    if (record.Locations == null || record.Locations.Count == 0)
                    {
                        // No locations, just skip
                        continue;
                    }

                    // NOTE: We DO NOT subtract local results because they may be needed to copy the file locally.
                    // This is because we don't decide to copy based on local results alone since the information may be stale.
                    PinResult remote = await PinRemoteAsync(operationContext, record, operationContext.Token, isLocal: lookup.Origin == GetBulkOrigin.Local);
                    if (remote.Succeeded)
                    {
                        return remote;
                    }
                }
                else
                {
                    Tracer.Warning(operationContext, $"Pin failed for hash {contentHash.ToShortString()}: directory query failed with error {lookup.ErrorMessage}");
                    return new PinResult(lookup);
                }
            }

            return PinResult.ContentNotFound;
        }

        /// <inheritdoc />
        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            OpenStreamResult streamResult =
                await Inner.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
            if (streamResult.Code == OpenStreamResult.ResultCode.Success)
            {
                return streamResult;
            }

            long? size = null;
            GetBulkLocationsResult localGetBulkResult = null;

            // First try to fetch file based on locally stored locations for the hash
            // Then fallback to fetching file based on global locations  (i.e. Redis) minus the locally stored locations which were already checked
            foreach (var getBulkTask in ContentLocationStore.MultiLevelGetLocations(operationContext, new[] { contentHash }, operationContext.Token, urgencyHint, subtractLocalResults: true))
            {
                var getBulkResult = await getBulkTask;
                // There is an issue with GetBulkLocationsResult construction from Exception that may loose the information about the origin.
                // So we rely on the result order of MultiLevelGetLocations method: the first result is always local and the second one is global.

                GetBulkOrigin origin = localGetBulkResult == null ? GetBulkOrigin.Local : GetBulkOrigin.Global;
                if (origin == GetBulkOrigin.Local)
                {
                    localGetBulkResult = getBulkResult;
                }

                // Local function: Use content locations for GetBulk to copy file locally
                async Task<BoolResult> tryCopyContentLocalAsync()
                {
                    if (!getBulkResult || !getBulkResult.ContentHashesInfo.Any())
                    {
                        return new BoolResult($"Metadata records for hash {contentHash.ToShortString()} not found in content location store.");
                    }

                    // Don't reconsider locally stored results that were checked in prior iteration
                    getBulkResult = getBulkResult.Subtract(localGetBulkResult);

                    var hashInfo = getBulkResult.ContentHashesInfo.Single();

                    var checkBulkResult = CheckBulkResult(operationContext, hashInfo, log: getBulkResult.Origin == GetBulkOrigin.Global);
                    if (!checkBulkResult.Succeeded)
                    {
                        return new BoolResult(checkBulkResult);
                    }

                    var copyResult = await TryCopyAndPutAsync(operationContext, hashInfo, operationContext.Token, urgencyHint, trace: false);
                    if (!copyResult)
                    {
                        return new BoolResult(copyResult);
                    }

                    size = copyResult.ContentSize;
                    return BoolResult.Success;
                }

                var copyLocalResult = await tryCopyContentLocalAsync();

                // Throw operation canceled to avoid operations below which are not value for canceled case.
                operationContext.Token.ThrowIfCancellationRequested();

                if (copyLocalResult.Succeeded)
                {
                    // Succeeded in copying content locally. No need to try with more content locations
                    break;
                }
                else if (origin == GetBulkOrigin.Global)
                {
                    return new OpenStreamResult(copyLocalResult, OpenStreamResult.ResultCode.ContentNotFound);
                }
            }

            Contract.Assert(size != null, "Size should be set if operation succeeded");

            var updateResult = await UpdateContentTrackerWithNewReplicaAsync(operationContext, new[] { new ContentHashWithSize(contentHash, size.Value) }, operationContext.Token, urgencyHint);
            if (!updateResult.Succeeded)
            {
                return new OpenStreamResult(updateResult);
            }

            return await Inner.OpenStreamAsync(operationContext, contentHash, operationContext.Token, urgencyHint);
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration pinOperationConfiguration)
        {
            return PinHelperAsync(operationContext, contentHashes, pinOperationConfiguration.UrgencyHint, pinOperationConfiguration);
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            return PinHelperAsync(operationContext, contentHashes, urgencyHint, PinOperationConfiguration.Default());
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinHelperAsync(OperationContext operationContext, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, PinOperationConfiguration pinOperationConfiguration)
        {
            Contract.Requires(contentHashes != null);

            IEnumerable<Task<Indexed<PinResult>>> pinResults = null;

            IEnumerable<Task<Indexed<PinResult>>> intermediateResult = null;
            if (pinOperationConfiguration.ReturnGlobalExistenceFast)
            {
                // Check globally for existence, but do not copy locally and do not update content tracker.
                pinResults = await Workflows.RunWithFallback(
                    contentHashes,
                    async hashes => {
                        intermediateResult = await Inner.PinAsync(operationContext, hashes, operationContext.Token, urgencyHint);
                        return intermediateResult;
                        },
                    hashes => _remotePinner(operationContext, hashes, operationContext.Token, succeedWithOneLocation: true, urgencyHint),
                    result => result.Succeeded);

                // Replace operation context with a new cancellation token so it can outlast this call
                operationContext = new OperationContext(operationContext.TracingContext, default);
            }

            // Default pin action
            var pinTask = Workflows.RunWithFallback(
                    contentHashes,
                    hashes => intermediateResult == null ? Inner.PinAsync(operationContext, hashes, operationContext.Token, urgencyHint) : Task.FromResult(intermediateResult),
                    hashes => _remotePinner(operationContext, hashes, operationContext.Token, succeedWithOneLocation: false, urgencyHint),
                    result => result.Succeeded,
                    // Exclude the empty hash because it is a special case which is hard coded for place/openstream/pin.
                    async hits => await UpdateContentTrackerWithLocalHitsAsync(operationContext, hits.Where(x => !contentHashes[x.Index].IsEmptyHash()).Select(x => new ContentHashWithSizeAndLastAccessTime(contentHashes[x.Index], x.Item.ContentSize, x.Item.LastAccessTime)).ToList(), operationContext.Token, urgencyHint));

            if (pinOperationConfiguration.ReturnGlobalExistenceFast)
            {
                // Fire off the default pin action, but do not await the result.
                pinTask.FireAndForget(operationContext);
            }
            else
            {
                pinResults = await pinTask;
            }

            Contract.Assert(pinResults != null);
            return pinResults;
        }

        /// <inheritdoc />
        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var resultWithData = await PerformPlaceFileGatedOperationAsync(
                operationContext,
                () => PlaceHelperAsync(
                    operationContext,
                    new[] { new ContentHashWithPath(contentHash, path) },
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter
                ));

            var result = await resultWithData.Result.SingleAwaitIndexed();
            result.Metadata = resultWithData.Metadata;
            return result;
        }

        /// <inheritdoc />
        protected override async Task<PlaceBulkResult> PlaceFileCoreAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // The fallback is invoked for cache misses only. This preserves existing behavior of
            // bubbling up errors with Inner store instead of trying remote.
            var resultWithData = await PerformPlaceFileGatedOperationAsync(
                operationContext,
                () => PlaceHelperAsync(
                    operationContext,
                    hashesWithPaths,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter
                ));

            // We are trying tracing here we did not want to change the signature for PlaceFileCoreAsync, which is implemented in multiple locations
            operationContext.TraceDebug($"PlaceFileBulk, Gate.OccupiedCount={resultWithData.Metadata.GateOccupiedCount} Gate.Wait={resultWithData.Metadata.GateWaitTime.TotalMilliseconds}ms");

            return resultWithData.Result;
        }

        private Task<PlaceBulkResult> PlaceHelperAsync(
            OperationContext operationContext,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            return Workflows.RunWithFallback(
                hashesWithPaths,
                args => Inner.PlaceFileAsync(
                    operationContext,
                    args,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    operationContext.Token,
                    urgencyHint),
                args => fetchFromMultiLevelContentLocationStoreThenPlaceFileAsync(args),
                result => IsPlaceFileSuccess(result),
                async hits => await UpdateContentTrackerWithLocalHitsAsync(
                    operationContext,
                    hits.Select(
                            x => new ContentHashWithSizeAndLastAccessTime(
                                hashesWithPaths[x.Index].Hash,
                                x.Item.FileSize,
                                x.Item.LastAccessTime))
                        .ToList(),
                    operationContext.Token,
                    urgencyHint));

            Task<PlaceBulkResult> fetchFromMultiLevelContentLocationStoreThenPlaceFileAsync(IReadOnlyList<ContentHashWithPath> fetchedContentInfo)
            {
                return MultiLevelUtilities.RunMultiLevelAsync(
                    fetchedContentInfo,
                    runFirstLevelAsync: args => FetchFromMultiLevelContentLocationStoreThenPutAsync(operationContext, args, urgencyHint, operationContext.Token),
                    runSecondLevelAsync: args => Inner.PlaceFileAsync(operationContext, args, accessMode, replacementMode, realizationMode, operationContext.Token, urgencyHint),
                    // NOTE: We just use the first level result if the the fetch using content location store fails because the place cannot succeed since the
                    // content will not have been put into the local CAS
                    useFirstLevelResult: result => !IsPlaceFileSuccess(result));
            }
        }

        private Task<ResultWithMetaData<PlaceBulkResult>> PerformPlaceFileGatedOperationAsync(OperationContext operationContext, Func<Task<PlaceBulkResult>> func, bool bulkPlace = true)
        {
            return PutAndPlaceFileGate.GatedOperationAsync( async(timeWaiting) =>
            {
                var gateOccupiedCount = Settings.MaximumConcurrentPutAndPlaceFileOperations - PutAndPlaceFileGate.CurrentCount;

                var result = await func();

                return new ResultWithMetaData<PlaceBulkResult>(
                    new ResultMetaData(timeWaiting, gateOccupiedCount),
                    result);
            }, operationContext.Token);
        }

        private static bool IsPlaceFileSuccess(PlaceFileResult result)
        { 
            return result.Code != PlaceFileResult.ResultCode.Error && result.Code != PlaceFileResult.ResultCode.NotPlacedContentNotFound;
        }

        /// <inheritdoc />
        public IEnumerable<ContentHash> EnumeratePinnedContentHashes()
        {
            return Inner is IHibernateContentSession session
                ? session.EnumeratePinnedContentHashes()
                : Enumerable.Empty<ContentHash>();
        }

        /// <inheritdoc />
        public Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes)
        {
            // TODO: Replace PinBulkAsync in hibernate with PinAsync bulk call (bug 1365340)
            return Inner is IHibernateContentSession session
                ? session.PinBulkAsync(context, contentHashes)
                : Task.FromResult(0);
        }

        private Task<PlaceBulkResult> FetchFromMultiLevelContentLocationStoreThenPutAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            UrgencyHint urgencyHint,
            CancellationToken token)
        {
            // First try to place file by fetching files based on locally stored locations for the hash
            // Then fallback to fetching file based on global locations  (i.e. Redis) minus the locally stored locations which were already checked

            var localGetBulkResult = new BuildXL.Utilities.AsyncOut<GetBulkLocationsResult>();

            return Workflows.RunWithFallback(
                hashesWithPaths,
                initialFunc: async args =>
                {
                    var contentHashes = args.Select(p => p.Hash).ToList();
                    localGetBulkResult.Value = await ContentLocationStore.GetBulkAsync(context, contentHashes, token, urgencyHint, GetBulkOrigin.Local);
                    return await FetchFromContentLocationStoreThenPutAsync(context, args, isLocal: true, urgencyHint, localGetBulkResult.Value, token);
                },
                fallbackFunc: async args =>
                {
                    var contentHashes = args.Select(p => p.Hash).ToList();
                    var globalGetBulkResult = await ContentLocationStore.GetBulkAsync(context, contentHashes, token, urgencyHint, GetBulkOrigin.Global);
                    globalGetBulkResult = globalGetBulkResult.Subtract(localGetBulkResult.Value);
                    return await FetchFromContentLocationStoreThenPutAsync(context, args, isLocal: false, urgencyHint, globalGetBulkResult, token);
                },
                isSuccessFunc: result => IsPlaceFileSuccess(result));
        }

        private async Task<PlaceBulkResult> FetchFromContentLocationStoreThenPutAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            bool isLocal,
            UrgencyHint urgencyHint,
            GetBulkLocationsResult getBulkResult,
            CancellationToken token)
        {
            try
            {
                // Tracing the hashes here for the entire list, instead of tracing one hash at a time inside TryCopyAndPutAsync method.

                // This returns failure if any item in the batch wasn't copied locally
                // TODO: split results and call PlaceFile on successfully copied files (bug 1365340)
                if (!getBulkResult.Succeeded || !getBulkResult.ContentHashesInfo.Any())
                {
                    return hashesWithPaths.Select(
                            p => new PlaceFileResult(
                                getBulkResult,
                                PlaceFileResult.ResultCode.NotPlacedContentNotFound,
                                "Metadata records not found in content location store"))
                        .AsIndexedTasks();
                }

                // TransformBlock is supposed to return items in FIFO order, so we don't need to index the input
                var copyFilesLocallyBlock =
                    new TransformBlock<Indexed<ContentHashWithSizeAndLocations>, Indexed<PlaceFileResult>>(
                        async indexed =>
                        {
                            var contentHashWithSizeAndLocations = indexed.Item;
                            PlaceFileResult result;
                            if (contentHashWithSizeAndLocations.Locations == null)
                            {
                                var message = $"No replicas ever registered for hash {contentHashWithSizeAndLocations.ContentHash.ToShortString()}";
                                
                                if (isLocal)
                                {
                                    // Trace only for locations obtained from the local store.
                                    Tracer.Warning(context, message);
                                }

                                result = new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, message);
                            }
                            else if (contentHashWithSizeAndLocations.Locations.Count == 0)
                            {
                                var message = $"No replicas exist currently in content tracker for hash {contentHashWithSizeAndLocations.ContentHash.ToShortString()}";

                                if (isLocal)
                                {
                                    // Trace only for locations obtained from the local store.
                                    Tracer.Warning(context, message);
                                }

                                result = new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, message);
                            }
                            else
                            {
                                var putResult = await TryCopyAndPutAsync(
                                    OperationContext(context),
                                    contentHashWithSizeAndLocations,
                                    token,
                                    urgencyHint,
                                    // We just traced all the hashes as a result of GetBulk call, no need to trace each individual hash.
                                    trace: false);
                                if (!putResult)
                                {
                                    result = new PlaceFileResult(putResult);
                                }
                                else
                                {
                                    result = new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithMove, putResult.ContentSize);
                                }

                            }

                            return result.WithIndex(indexed.Index);
                        },
                        new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = Settings.ParallelCopyFilesLimit, });

                // TODO: Better way ? (bug 1365340)
                copyFilesLocallyBlock.PostAll(getBulkResult.ContentHashesInfo.AsIndexed());
                var copyFilesLocally =
                    await Task.WhenAll(
                        Enumerable.Range(0, getBulkResult.ContentHashesInfo.Count).Select(i => copyFilesLocallyBlock.ReceiveAsync(token)));
                copyFilesLocallyBlock.Complete();

                var updateResults = await UpdateContentTrackerWithNewReplicaAsync(
                    context,
                    copyFilesLocally.Where(r => r.Item.Succeeded).Select(r => new ContentHashWithSize(hashesWithPaths[r.Index].Hash, r.Item.FileSize)).ToList(),
                    token,
                    urgencyHint);

                if (!updateResults.Succeeded)
                {
                    return copyFilesLocally.Select(result => new PlaceFileResult(updateResults).WithIndex(result.Index)).AsTasks();
                }

                return copyFilesLocally.AsTasks();
            }
            catch (Exception ex)
            {
                return hashesWithPaths.Select((hash, index) => new PlaceFileResult(ex).WithIndex(index)).AsTasks();
            }
        }

        private BoolResult CheckBulkResult(Context context, ContentHashWithSizeAndLocations result, bool log = true)
        {
            // Null represents no replicas were ever registered, where as empty list implies content is missing from all replicas
            if (result.Locations == null)
            {
                if (log)
                {
                    Tracer.Warning(context, $"No replicas found in content tracker for hash {result.ContentHash.ToShortString()}");
                }

                return new BoolResult($"No replicas registered for hash");
            }

            if (!result.Locations.Any())
            {
                if (log)
                {
                    Tracer.Warning(context, $"No replicas currently exist in content tracker for hash {result.ContentHash.ToShortString()}");
                }

                return new BoolResult($"Content for hash is missing from all replicas");
            }

            return BoolResult.Success;
        }

        private async Task<PutResult> TryCopyAndPutAsync(Context context, ContentHashWithSizeAndLocations hashInfo, CancellationToken cts, UrgencyHint urgencyHint, bool trace = true)
        {
            if (trace)
            {
                Tracer.Debug(context, $"Copying {hashInfo.ContentHash.ToShortString()} with {hashInfo.Locations.Count} locations");
            }

            using (var operationContext = TrackShutdown(context, cts))
            {
                if (ContentLocationStore.AreBlobsSupported && hashInfo.Size > 0 && hashInfo.Size <= ContentLocationStore.MaxBlobSize)
                {
                    var smallFileResult = await ContentLocationStore.GetBlobAsync(operationContext, hashInfo.ContentHash);

                    if (smallFileResult.Succeeded && smallFileResult.Found)
                    {
                        using (var stream = new MemoryStream(smallFileResult.Blob))
                        {
                            return await Inner.PutStreamAsync(context, hashInfo.ContentHash, stream, cts, urgencyHint);
                            
                        }
                    }
                }

                byte[] bytes = null;

                var putResult = await DistributedCopier.TryCopyAndPutAsync(
                    operationContext,
                    _copierHost,
                    hashInfo,
                    handleCopyAsync: async args =>
                    {
                        (CopyFileResult copyFileResult, AbsolutePath tempLocation, int attemptCount) = args;

                        PutResult innerPutResult;
                        long actualSize = copyFileResult.Size ?? hashInfo.Size;
                        if (Settings.UseTrustedHash(actualSize) && Inner is ITrustedContentSession trustedInner)
                        {
                            // The file has already been hashed, so we can trust the hash of the file.
                            innerPutResult = await trustedInner.PutTrustedFileAsync(context, new ContentHashWithSize(hashInfo.ContentHash, actualSize), tempLocation, FileRealizationMode.Move, cts, urgencyHint);

                            // BytesFromTrustedCopy will only be non-null when the trusted copy exposes the bytes it copied because AreBlobsSupported evaluated to true
                            //  and the file size is smaller than BlobMaxSize.
                            if (innerPutResult && copyFileResult.BytesFromTrustedCopy != null)
                            {
                                bytes = copyFileResult.BytesFromTrustedCopy;
                            }
                        }
                        else
                        {
                            // Pass the HashType, not the Hash. This prompts a re-hash of the file, which places it where its actual hash requires.
                            // If the actual hash differs from the expected hash, then we fail below and move to the next location.
                            // Also, record the bytes if the file is small enough to be put into the ContentLocationStore.
                            if (actualSize >= 0 && actualSize <= ContentLocationStore.MaxBlobSize && ContentLocationStore.AreBlobsSupported && Inner is IDecoratedStreamContentSession decoratedStreamSession)
                            {
                                RecordingStream recorder = null;
                                innerPutResult = await decoratedStreamSession.PutFileAsync(
                                    context,
                                    tempLocation,
                                    hashInfo.ContentHash.HashType,
                                    FileRealizationMode.Move,
                                    cts,
                                    urgencyHint,
                                    stream =>
                                    {
                                        recorder = RecordingStream.ReadRecordingStream(inner: stream, size: actualSize);
                                        return recorder;
                                    });

                                if (innerPutResult && recorder != null)
                                {
                                    bytes = recorder.RecordedBytes;
                                }
                            }
                            else
                            {
                                innerPutResult = await Inner.PutFileAsync(context, hashInfo.ContentHash.HashType, tempLocation, FileRealizationMode.Move, cts, urgencyHint);
                            }
                        }

                        return innerPutResult;

                    },
                    handleBadLocations: badContentLocations =>
                    {
                        Tracer.Debug(
                            operationContext.Context,
                            $"Removing bad content locations for content hash {hashInfo.ContentHash.ToShortString()}: {string.Join(",", badContentLocations)}");
                        _backgroundTaskTracker.Add(
                            () =>
                                ContentLocationStore.TrimBulkAsync(
                                    operationContext.Context,
                                    new[] { new ContentHashAndLocations(hashInfo.ContentHash, badContentLocations) },
                                    CancellationToken.None,
                                    UrgencyHint.Low));
                    });

                if (bytes != null && putResult.Succeeded)
                {
                    await PutBlobAsync(operationContext, putResult.ContentHash, bytes);
                }

                return putResult;
            }
        }

        /// <summary>
        /// Puts a given blob into redis (either inline or not depending on the settings).
        /// </summary>
        /// <remarks>
        /// This method won't fail, because all the errors are traced and swallowed.
        /// </remarks>
        protected async Task PutBlobAsync(OperationContext context, ContentHash contentHash, byte[] bytes)
        {
            if (Settings.InlinePutBlobs)
            {
                // Failures already traced. No need to trace it here one more time.
                await ContentLocationStore.PutBlobAsync(context, contentHash, bytes).IgnoreFailure();
            }
            else
            {
                // Fire and forget since this step is optional.
                // Since the rest of the operation is done asynchronously, create new context to stop cancelling operation prematurely.
                WithOperationContext(
                        context,
                        CancellationToken.None,
                        opContext => ContentLocationStore.PutBlobAsync(opContext, contentHash, bytes)
                    )
                    // Tracing unhandled errors only because normal failures already traced by the operation provider.
                    .TraceIfFailure(context, failureSeverity: Severity.Debug, traceTaskExceptionsOnly: true, operation: "PutBlobAsync");
            }
        }

        private Task<BoolResult> UpdateContentTrackerWithNewReplicaAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint)
        {
            if (contentHashes.Count == 0)
            {
                return BoolResult.SuccessTask;
            }

            // TODO: Pass location store option (seems to only be used to prevent updating TTL when replicating for proactive replication) (bug 1365340)
            return ContentLocationStore.RegisterLocalLocationAsync(context, contentHashes, cts, urgencyHint);
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> PinFromMultiLevelContentLocationStore(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            bool succeedWithOneLocation,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            var operationContext = new OperationContext(context, cts);

            return Workflows.RunWithFallback(
                contentHashes,
                hashes => PinFromContentLocationStoreOriginAsync(operationContext, hashes, cts, GetBulkOrigin.Local, succeedWithOneLocation: succeedWithOneLocation, urgencyHint),
                hashes => PinFromContentLocationStoreOriginAsync(operationContext, hashes, cts, GetBulkOrigin.Global, succeedWithOneLocation: succeedWithOneLocation, urgencyHint),
                result => result.Succeeded);
        }

        // This method creates pages of hashes, makes one bulk call to the content location store to get content location record sets for all the hashes on the page,
        // and fires off processing of the returned content location record sets while proceeding to the next page of hashes in parallel.
        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinFromContentLocationStoreOriginAsync(OperationContext operationContext, IReadOnlyList<ContentHash> hashes, CancellationToken cancel, GetBulkOrigin origin, bool succeedWithOneLocation, UrgencyHint urgency = UrgencyHint.Nominal)
        {
            // Create an action block to process all the requested remote pins while limiting the number of simultaneously executed.
            var pinnings = new List<RemotePinning>(hashes.Count);
            var pinningOptions = new ExecutionDataflowBlockOptions() { CancellationToken = cancel, MaxDegreeOfParallelism = Settings.PinConfiguration?.MaxIOOperations ?? 1 };
            var pinningAction = new ActionBlock<RemotePinning>(async pinning => await PinRemoteAsync(operationContext, pinning, cancel, isLocal: origin == GetBulkOrigin.Local, updateContentTracker: false, succeedWithOneLocation: succeedWithOneLocation), pinningOptions);
            
            // Process the requests in pages so we can make bulk calls, but not too big bulk calls, to the content location store.
            foreach (IReadOnlyList<ContentHash> pageHashes in hashes.GetPages(ContentLocationStore.PageSize))
            {
                // Make a bulk call to content location store to get location records for all hashes on the page.
                // NOTE: We use GetBulkStackedAsync so that when Global results are retrieved we also include Local results to ensure we get a full view of available content
                GetBulkLocationsResult pageLookup = await ContentLocationStore.GetBulkStackedAsync(operationContext, pageHashes, cancel, urgency, origin);

                // If successful, fire off the remote pinning logic for each hash. If not, set all pins to failed.
                if (pageLookup.Succeeded)
                {
                    foreach (ContentHashWithSizeAndLocations record in pageLookup.ContentHashesInfo)
                    {
                        RemotePinning pinning = new RemotePinning() { Record = record };
                        pinnings.Add(pinning);
                        bool accepted = await pinningAction.SendAsync(pinning, cancel);
                        Contract.Assert(accepted);
                    }
                }
                else
                {
                    foreach (ContentHash hash in pageHashes)
                    {
                        Tracer.Warning(operationContext, $"Pin failed for hash {hash.ToShortString()}: directory query failed with error {pageLookup.ErrorMessage}");
                        RemotePinning pinning = new RemotePinning() { Record = new ContentHashWithSizeAndLocations(hash, -1L), Result = new PinResult(pageLookup) };
                        pinnings.Add(pinning);
                    }
                }
            }

            Contract.Assert(pinnings.Count == hashes.Count);

            // Wait for all the pinning actions to complete.
            pinningAction.Complete();

            try
            {
                await pinningAction.Completion;
            }
            catch (TaskCanceledException)
            {
                // Cancellation token provided to an action block can be canceled.
                // Ignoring the exception in this case.
            }

            // Inform the content directory that we copied the files.
            // Looking for distributed pin results that were successful by copying the content locally.
            var localCopies = pinnings.Select((rp, index) => (result: rp, index)).Where(x => x.result.Result is DistributedPinResult dpr && dpr.CopyLocally).ToList();

            BoolResult updated = await UpdateContentTrackerWithNewReplicaAsync(operationContext, localCopies.Select(lc => new ContentHashWithSize(lc.result.Record.ContentHash, lc.result.Record.Size)).ToList(), cancel, UrgencyHint.Nominal);
            if (!updated.Succeeded)
            {
                // We failed to update the tracker. Need to update the results.
                string hashesAsString = string.Join(", ", localCopies.Select(lc => lc.result.Record.ContentHash.ToShortString()));
                Tracer.Warning(operationContext, $"Pin failed for hashes {hashesAsString}: local copy succeeded, but could not inform content directory due to {updated.ErrorMessage}.");
                foreach (var (_, index) in localCopies)
                {
                    pinnings[index].Result = new PinResult(updated);
                }
            }

            // The return type should probably be just Task<IList<PinResult>>, but higher callers require the Indexed wrapper and that the PinResults be encased in Tasks.
            return pinnings.Select(x => x.Result ?? createCanceledPutResult()).AsIndexed().AsTasks();

            static PinResult createCanceledPutResult() => new ErrorResult("The operation was canceled").AsResult<PinResult>();
        }

        // The dataflow framework can process only a single object, and returns no output from that processing. By combining the input and output of each remote pinning into a single object,
        // we can nonetheless use the dataflow framework to process pinnings and read the output from the updated objects afterward.
        private class RemotePinning
        {
            public ContentHashWithSizeAndLocations Record { get; set; }

            public PinResult Result { get; set; }
        }

        // This method processes each remote pinning, setting the output when the operation is completed.
        private async Task PinRemoteAsync(
            OperationContext context,
            RemotePinning pinning,
            CancellationToken cancel,
            bool isLocal,
            bool updateContentTracker = true,
            bool succeedWithOneLocation = false)
        {
            PinResult result = await PinRemoteAsync(context, pinning.Record, cancel, isLocal, updateContentTracker, succeedWithOneLocation: succeedWithOneLocation);
            pinning.Result = result;
        }

        // This method processes a single content location record set for pinning.
        private async Task<PinResult> PinRemoteAsync(
            OperationContext operationContext,
            ContentHashWithSizeAndLocations remote,
            CancellationToken cancel,
            bool isLocal,
            bool updateContentTracker = true,
            bool succeedWithOneLocation = false)
        {
            Contract.Requires(remote != null);

            IReadOnlyList<MachineLocation> locations = remote.Locations;

            // If no remote locations are recorded, we definitely can't pin
            if (locations == null || locations.Count == 0)
            {
                if (!isLocal)
                {
                    // Trace only when pin failed based on the data from the global store.
                    Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: no remote records.");
                }

                return PinResult.ContentNotFound;
            }

            if (Settings.PinConfiguration == null)
            {
                if (Settings.ContentAvailabilityGuarantee == ContentAvailabilityGuarantee.FileRecordsExist)
                {
                    return PinResult.Success;
                }
                else if (Settings.ContentAvailabilityGuarantee == ContentAvailabilityGuarantee.RedundantFileRecordsOrCheckFileExistence)
                {
                    if (locations.Count >= Settings.AssumeAvailableReplicaCount)
                    {
                        return PinResult.Success;
                    }

                    var verify = await VerifyAsync(operationContext, remote, cancel);
                    return verify.Present.Count > 0 ? PinResult.Success : PinResult.ContentNotFound;
                }
                else
                {
                    throw Contract.AssertFailure($"Unknown enum value: {Settings.ContentAvailabilityGuarantee}");
                }
            }

            // When we only require the content to exist at least once anywhere, we can ignore pin thresholds
            // and return success after finding a single location.
            if (succeedWithOneLocation && locations.Count >= 1)
            {
                // Do not update pin cache because this does not represent complete knowledge about this context.
                return DistributedPinResult.Success($"{locations.Count} replicas (global succeeds)");
            }

            // TODO (minlam): Transitional phase, we will be removing the logic that uses PinRisk to compute MinVerifiedCount, MinUnverifiedCount, and PinCacheTimeToLive
            if (Settings.PinConfiguration.PinMinUnverifiedCount.HasValue)
            {
                var minUnverifiedCount = Settings.PinConfiguration.PinMinUnverifiedCount.Value;
                if (locations.Count >= minUnverifiedCount)
                {
                    _counters[Counters.PinUnverifiedCountSatisfied].Increment();
                    return DistributedPinResult.Success($"Replica Count={locations.Count}");
                }

            }
            else
            {
                // Calculate the minimum number of remote verified and unverified copies for us to
                // return a successful pin at the given risk level.
                ComputePinThresholds(remote, Settings.PinConfiguration.PinRisk, out var minVerifiedCount, out var minUnverifiedCount, out var pinCacheTimeToLive);
                Contract.Assert(minVerifiedCount > 0);
                Contract.Assert(minUnverifiedCount >= minVerifiedCount);

                // If we have enough records globally, we are satisfied without further action.
                if (locations.Count >= minUnverifiedCount)
                {
                    _pinCache?.SetPinInfo(remote.ContentHash, pinCacheTimeToLive);
                    return DistributedPinResult.Success($"{locations.Count} replicas");
                }

                // If we have enough records that we would be satisfied if they were verified, verify them.
                // Skip this step if no IO slots are available; if we would have to spend time waiting on them, we might as well just move on to copying.
                if (locations.Count >= minVerifiedCount && DistributedCopier.CurrentIoGateCount > 0)
                {
                    var verify = await VerifyAsync(operationContext, remote, cancel);

                    if (verify.Present.Count >= minVerifiedCount)
                    {
                        return DistributedPinResult.Success($"{verify.Present.Count} verified remote copies >= {minVerifiedCount} required");
                    }

                    if (verify.Present.Count == 0 && verify.Unknown.Count == 0 && isLocal)
                    {
                        Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: all remote copies absent.");
                        return PinResult.ContentNotFound;
                    }

                    // We are going to try to copy. Have the copier try the verified present locations first, then the unknown locations.
                    // Don't give it the verified absent locations.
                    List<MachineLocation> newLocations = new List<MachineLocation>();
                    newLocations.AddRange(verify.Present);
                    newLocations.AddRange(verify.Unknown);
                    remote = new ContentHashWithSizeAndLocations(remote.ContentHash, remote.Size, newLocations);
                }

            }

            if (isLocal)
            {
                // Don't copy content locally based on locally cached result. So stop here and return content not found.
                // This method will be called again with global locations at which time we will attempt to copy the files locally.
                // When allowing global locations to succeed a put, report success.
                return PinResult.ContentNotFound;
            }

            // Previous checks were not sufficient, so copy the file locally.
            PutResult copy = await TryCopyAndPutAsync(operationContext, remote, cancel, UrgencyHint.Nominal, trace: false);
            if (copy)
            {
                if (!updateContentTracker)
                {
                    return DistributedPinResult.SuccessByLocalCopy();
                }

                // Inform the content directory that we have the file.
                // We wait for this to complete, rather than doing it fire-and-forget, because another machine in the ring may need the pinned content immediately.
                BoolResult updated = await UpdateContentTrackerWithNewReplicaAsync(operationContext, new[] { new ContentHashWithSize(remote.ContentHash, copy.ContentSize) }, cancel, UrgencyHint.Nominal);
                if (updated.Succeeded)
                {
                    // Initiate a proactive copy if just pinned content is under-replicated
                    if (Settings.ProactiveCopyOnPin && Settings.ProactiveCopyMode != ProactiveCopyMode.Disabled)
                    {
                        // Since the rest of the operation is done asynchronously, create new context to stop cancelling operation prematurely.
                        var proactiveCopyTask = WithOperationContext(
                            operationContext,
                            CancellationToken.None,
                            opContext => ProactiveCopyIfNeededAsync(opContext, remote.ContentHash, tryBuildRing: true, ProactiveCopyReason.Pin));

                        if (Settings.InlineProactiveCopies)
                        {
                            var proactiveCopyResult = await proactiveCopyTask;

                            // Only fail if all copies failed.
                            if (!proactiveCopyResult.Succeeded && proactiveCopyResult.RingCopyResult?.Succeeded == false && proactiveCopyResult.OutsideRingCopyResult?.Succeeded == false)
                            {
                                return new PinResult(proactiveCopyResult);
                            }
                        }
                        else
                        {
                            proactiveCopyTask.FireAndForget(operationContext);
                        }
                    }

                    return DistributedPinResult.SuccessByLocalCopy();
                }
                else
                {
                    // Tracing the error separately.
                    Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: local copy succeeded, but could not inform content directory due to {updated.ErrorMessage}.");
                    return new PinResult(updated);
                }
            }
            else
            {
                // Tracing the error separately.
                Tracer.Warning(operationContext, $"Pin failed for hash {remote.ContentHash.ToShortString()}: local copy failed with {copy}.");
                return PinResult.ContentNotFound;
            }
        }

        // Compute the minimum number of records for us to proceed with a pin with and without record verification.
        // In this implementation, there are two risks: the machineRisk that a machine cannot be contacted when the file is needed (e.g. network error or service reboot), and
        // the fileRisk that the file is not actually present on the machine despite the record (e.g. the file has been deleted or the machine re-imaged). The second risk
        // can be mitigated by verifying that the files actually exist, but the first cannot. The verifiedRisk of not getting the file from a verified location is thus equal to
        // the machineRisk, while the unverfiedRisk of not getting a file from an unverified location is larger. Given n machines each with risk q, the risk Q of not getting
        // the file from any of them is Q = q^n. Solving for n to get the number of machines required to achieve a given overall risk tolerance gives n = ln Q / ln q.
        // In this way we can compute the minimum number of verified and unverified records to return a successful pin.
        // Future refinements of this method could use machine reputation and file lifetime knowledge to improve this model.
        private void ComputePinThresholds(ContentHashWithSizeAndLocations remote, double risk, out int minVerifiedCount, out int minUnverifiedCount, out TimeSpan pinCacheTimeToLive)
        {
            Contract.Assert(Settings.PinConfiguration != null);
            Contract.Assert(remote != null);
            Contract.Assert((risk > 0.0) && (risk < 1.0));

            double verifiedRisk = Settings.PinConfiguration.MachineRisk;
            double unverifiedRisk = Settings.PinConfiguration.MachineRisk + (Settings.PinConfiguration.FileRisk * (1.0 - Settings.PinConfiguration.MachineRisk));

            Contract.Assert((verifiedRisk > 0.0) && (verifiedRisk < 1.0));
            Contract.Assert((unverifiedRisk > 0.0) && (unverifiedRisk < 1.0));
            Contract.Assert(unverifiedRisk >= verifiedRisk);

            double lnRisk = Math.Log(risk);
            double lnVerifiedRisk = Math.Log(verifiedRisk);
            double lnUnverifiedRisk = Math.Log(unverifiedRisk);

            minVerifiedCount = (int)Math.Ceiling(lnRisk / lnVerifiedRisk);
            minUnverifiedCount = (int)Math.Ceiling(lnRisk / lnUnverifiedRisk);

            if (_pinCache == null || Settings.PinConfiguration.PinCachePerReplicaRetentionCreditMinutes <= 0)
            {
                pinCacheTimeToLive = TimeSpan.Zero;
            }
            else
            {
                // Pin cache time to live is:
                // r * (1 + d + d^2 + ... + d^n-1) = r * (1 - d^n) / (1 - d)
                // where
                // r = PinCachePerReplicaRetentionCreditMinutes
                // d = PinCacheReplicaCreditRetentionFactor
                // n = replica count
                var decay = Settings.PinConfiguration.PinCacheReplicaCreditRetentionFactor;
                var pinCacheTimeToLiveMinutes = Settings.PinConfiguration.PinCachePerReplicaRetentionCreditMinutes * (1 - Math.Pow(decay, remote.Locations.Count)) / (1 - decay);
                pinCacheTimeToLive = TimeSpan.FromMinutes(pinCacheTimeToLiveMinutes);
            }
        }

        // Given a content record set, check all the locations and determine, for each location, whether the file is actually
        // present, actually absent, or if its presence or absence cannot be determined in the alloted time.
        // The CheckFileExistsAsync method that is called in this implementation may be doing more complicated stuff (retries, queuing,
        // throttling, its own timeout) than we want or expect; we should dig into this.
        private async Task<DistributedContentCopier<T>.VerifyResult> VerifyAsync(Context context, ContentHashWithSizeAndLocations remote, CancellationToken cancel)
        {
            var verifyResult = await DistributedCopier.VerifyAsync(context, remote, cancel);

            var absent = verifyResult.Absent;
            if (absent.Count > 0)
            {
                Tracer.Info(context, $"For hash {remote.ContentHash.ToShortString()}, removing records for locations from which content is verified missing: {string.Join(",", absent)}");
                _backgroundTaskTracker.Add(() => ContentLocationStore.TrimBulkAsync(context, new[] { new ContentHashAndLocations(remote.ContentHash, absent) }, CancellationToken.None, UrgencyHint.Low));
            }

            return verifyResult;
        }

        private Task UpdateContentTrackerWithLocalHitsAsync(Context context, IReadOnlyList<ContentHashWithSizeAndLastAccessTime> contentHashesWithInfo, CancellationToken cts, UrgencyHint urgencyHint)
        {
            IReadOnlyList<ContentHashWithSize> hashesToEagerUpdate;

            if (Disposed)
            {
                // Nothing to do.
                return BoolTask.True;
            }

            if (contentHashesWithInfo.Count == 0)
            {
                // Nothing to do.
                return BoolTask.True;
            }

            if (_contentTrackerUpdater != null)
            {
                // Filter out hashes that can be lazily updated based on content's local age.
                hashesToEagerUpdate = _contentTrackerUpdater.ScheduleHashTouches(context, contentHashesWithInfo).ToList();
                Tracer.Debug(context, $"Updating {hashesToEagerUpdate.Count}/{contentHashesWithInfo.Count} in the content tracker eagerly");

                if (hashesToEagerUpdate.Count == 0)
                {
                    // No eager hashes, just return.
                    return BoolTask.True;
                }
            }
            else
            {
                hashesToEagerUpdate = contentHashesWithInfo.Select(x => new ContentHashWithSize(x.Hash, x.Size)).ToList();
            }

            // Wait for update to complete on remaining hashes to cover case where the record has expired and another machine in the ring requests it immediately after this pin succeeds.
            return UpdateContentTrackerWithNewReplicaAsync(context, hashesToEagerUpdate, cts, urgencyHint);
        }

        internal Task<ProactiveCopyResult> ProactiveCopyIfNeededAsync(
            OperationContext context, 
            ContentHash hash, 
            bool tryBuildRing, 
            ProactiveCopyReason reason, 
            string path = null)
        {
            if (!_pendingProactivePuts.Add(hash))
            {
                return Task.FromResult(ProactiveCopyResult.CopyNotRequiredResult);
            }

            return context.PerformOperationAsync(
                Tracer,
                traceErrorsOnly: !Settings.TraceProactiveCopy,
                operation: async () =>
                {
                    try
                    {
                        var hashArray = _buildIdHash != null && tryBuildRing
                            ? new[] { hash, _buildIdHash.Value }
                            : new[] { hash };

                        // First check in local location store, then global if failed.
                        var getLocationsResult = await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Local);
                        if (getLocationsResult.Succeeded && getLocationsResult.ContentHashesInfo[0].Locations.Count >= Settings.ProactiveCopyLocationsThreshold)
                        {
                            _counters[Counters.GetLocationsSatisfiedFromLocal].Increment();
                            return ProactiveCopyResult.CopyNotRequiredResult;
                        }
                        else
                        {
                            getLocationsResult += await ContentLocationStore.GetBulkAsync(context, hashArray, context.Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ThrowIfFailure();
                            _counters[Counters.GetLocationsSatisfiedFromRemote].Increment();
                        }

                        if (getLocationsResult.ContentHashesInfo[0].Locations.Count > Settings.ProactiveCopyLocationsThreshold)
                        {
                            return ProactiveCopyResult.CopyNotRequiredResult;
                        }

                        IReadOnlyList<MachineLocation> buildRingMachines = null;
                        var replicatedLocations = getLocationsResult.ContentHashesInfo[0].Locations;

                        // Get random machine inside build ring
                        Task<PushFileResult> insideRingCopyTask;
                        if (tryBuildRing && (Settings.ProactiveCopyMode & ProactiveCopyMode.InsideRing) != 0)
                        {
                            if (_buildIdHash != null)
                            {
                                buildRingMachines = getLocationsResult.ContentHashesInfo[getLocationsResult.ContentHashesInfo.Count - 1].Locations;
                                var candidates = buildRingMachines
                                    .Where(m => !m.Equals(LocalCacheRootMachineLocation))
                                    .Where(m => ContentLocationStore.IsMachineActive(m)).ToArray();

                                if (candidates.Length > 0)
                                {
                                    var candidate = candidates[ThreadSafeRandom.Generator.Next(0, candidates.Length)];
                                    insideRingCopyTask = RequestOrPushContentAsync(context, hash, candidate, isInsideRing: true, reason, ProactiveCopyLocationSource.Random);
                                }
                                else
                                {
                                    insideRingCopyTask = Task.FromResult(new PushFileResult($"Could not find any machines belonging to the build ring for build {_buildId}."));
                                }
                            }
                            else
                            {
                                insideRingCopyTask = Task.FromResult(new PushFileResult("BuildId was not specified, so machines in the build ring cannot be found."));
                            }
                        }
                        else
                        {
                            insideRingCopyTask = Task.FromResult(PushFileResult.Disabled());
                        }

                        buildRingMachines ??= new[] { LocalCacheRootMachineLocation };

                        Task<PushFileResult> outsideRingCopyTask;
                        if ((Settings.ProactiveCopyMode & ProactiveCopyMode.OutsideRing) != 0)
                        {
                            Result<MachineLocation> getLocationResult = null;
                            var source = ProactiveCopyLocationSource.Random;

                            // Try to select machine from prediction store.
                            if (_predictionStore != null && path != null)
                            {
                                var machines = _predictionStore.GetTargetMachines(context, path);
                                if (machines?.Count > 0)
                                {
                                    var index = ThreadSafeRandom.Generator.Next(0, machines.Count);
                                    getLocationResult = new MachineLocation(machines[index]);
                                    source = ProactiveCopyLocationSource.PredictionStore;
                                }
                            }

                            // Try to select one of the designated machines for this hash.
                            if (Settings.ProactiveCopyUsePreferredLocations && getLocationResult?.Succeeded != true)
                            {
                                var designatedLocationsResult = ContentLocationStore.GetDesignatedLocations(hash);
                                if (designatedLocationsResult)
                                {
                                    var candidates = designatedLocationsResult.Value
                                        .Except(replicatedLocations).ToArray();

                                    if (candidates.Length > 0)
                                    {
                                        getLocationResult = candidates[ThreadSafeRandom.Generator.Next(0, candidates.Length)];
                                        source = ProactiveCopyLocationSource.DesignatedLocation;
                                        _counters[Counters.ProactiveCopy_OutsideRingFromPreferredLocations].Increment();
                                    }
                                }
                            }

                            // Try to select one machine at random.
                            if (getLocationResult?.Succeeded != true)
                            {
                                // Make sure that the machine is not in the build ring and does not already have the content.
                                var machinesToSkip = getLocationsResult.ContentHashesInfo[0].Locations.Concat(buildRingMachines).ToArray();
                                getLocationResult = ContentLocationStore.GetRandomMachineLocation(except: machinesToSkip);
                                source = ProactiveCopyLocationSource.Random;
                            }

                            if (getLocationResult.Succeeded)
                            {
                                var candidate = getLocationResult.Value;
                                outsideRingCopyTask = RequestOrPushContentAsync(context, hash, candidate, isInsideRing: false, reason, source);
                            }
                            else
                            {
                                outsideRingCopyTask = Task.FromResult(new PushFileResult(getLocationResult));
                            }
                        }
                        else
                        {
                            outsideRingCopyTask = Task.FromResult(PushFileResult.Disabled());
                        }

                        return new ProactiveCopyResult(await insideRingCopyTask, await outsideRingCopyTask, getLocationsResult.ContentHashesInfo[0].Entry);
                    }
                    finally
                    {
                        _pendingProactivePuts.Remove(hash);
                    }
                },
                extraEndMessage: r => $"Reason=[{reason}]");
        }

        private async Task<PushFileResult> RequestOrPushContentAsync(OperationContext context, ContentHash hash, MachineLocation target, bool isInsideRing, ProactiveCopyReason reason, ProactiveCopyLocationSource source)
        {
            if (Settings.PushProactiveCopies)
            {
                return await DistributedCopier.PushFileAsync(
                    context,
                    hash,
                    target,
                    async () =>
                    {
                        var streamResult = await Inner.OpenStreamAsync(context, hash, context.Token);
                        if (streamResult.Succeeded)
                        {
                            return streamResult.Stream;
                        }

                        return new Result<Stream>(streamResult);
                    },
                    isInsideRing,
                    reason,
                    source);
            }
            else
            {
                var requestResult = await DistributedCopier.RequestCopyFileAsync(context, hash, target, isInsideRing);
                if (requestResult)
                {
                    return PushFileResult.PushSucceeded();
                }

                return new PushFileResult(requestResult, "Failed requesting a copy");
            }
        }

        /// <inheritdoc />
        protected override CounterSet GetCounters() =>
            base.GetCounters()
                .Merge(DistributedCopier.GetCounters())
                .Merge(_counters.ToCounterSet());
    }
}
