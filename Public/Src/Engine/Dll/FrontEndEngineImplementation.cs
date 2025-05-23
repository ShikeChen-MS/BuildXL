// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.ProcessPipExecutor;
using BuildXL.Storage;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using static BuildXL.Utilities.Core.BuildParameters;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// This class implements the FrontEndEngineAbstraction
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed class FrontEndEngineImplementation : FrontEndEngineAbstraction, IDisposable
    {
        private readonly InputTracker m_inputTracker;

        /// <summary>
        /// Path table.
        /// </summary>
        public readonly PathTable PathTable;

        private FileCombiner m_specCache;

        // The FileContentTable is a func because the initialization logic and lifetime of this class has gotten very complex.
        private readonly Func<FileContentTable> m_getFileContentTable;

        private readonly bool m_isPartialReuse;
        
        private readonly IReadOnlyDictionary<string, bool> m_frontendsEnvironmentRestriction;
        private readonly IFrontEndController m_frontEndController;

        /// <summary>
        /// All build parameters at creation time.
        /// </summary>
        /// <remarks>
        /// This is a mix of the environment and explict arguments.
        /// The tracked values are indicative of usage in the configuration.
        /// </remarks>
        private readonly ConcurrentDictionary<string, TrackedValue> m_allBuildParameters;

        /// <summary>
        /// Limited set of environment variables based on what is allowed to be accessed
        /// </summary>
        private IReadOnlyDictionary<string, TrackedValue> m_visibleBuildParameters;

        /// <summary>
        /// Mappings of mounts used by frontend.
        /// </summary>
        /// <remarks>
        /// This map ignores module id. This is OK because currently module id is ignored during the evaluation; see AmbientContext.
        /// TODO: Include module id as part of the key.
        /// </remarks>
        private readonly ConcurrentDictionary<string, IMount> m_usedMounts = new ConcurrentDictionary<string, IMount>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// File name of the SpecCache
        /// </summary>
        public const string SpecCacheFileName = "SpecCache";

        private bool m_finishedBuildParameterTracking;

        /// <summary>
        /// Used for materializing files from the cache with tracking
        /// </summary>
        private readonly LocalDiskContentStore m_localDiskContentStore;

        private readonly ActionBlockSlim<MaterializeFileRequest> m_localDiskContentStoreConcurrencyLimiter;

        /// <summary>
        /// Contains the mounts.
        /// </summary>
        private readonly MountsTable m_mountsTable;

        private readonly LoggingContext m_loggingContext;
        private readonly DirectoryTranslator m_directoryTranslator;

        private readonly ReparsePointResolver m_reparsePointResolver;

        /// <summary>
        /// Creates an instance of <see cref="FrontEndEngineImplementation"/>.
        /// </summary>
        public FrontEndEngineImplementation(
            LoggingContext loggingContext,
            PathTable pathTable,
            IConfiguration configuration,
            IStartupConfiguration startupConfiguration,
            MountsTable mountsTable,
            InputTracker inputTracker,
            DirectoryTranslator directoryTranslator,
            Func<FileContentTable> getFileContentTable,
            int timerUpdatePeriod,
            bool isPartialReuse,
            IFrontEndController frontEndController)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(configuration != null);
            Contract.Requires(startupConfiguration != null);
            Contract.Requires(mountsTable != null);
            Contract.Requires(inputTracker != null);
            Contract.Requires(getFileContentTable != null);
            Contract.Requires(frontEndController != null);

            m_loggingContext = loggingContext;
            PathTable = pathTable;
            m_mountsTable = mountsTable;
            m_inputTracker = inputTracker;
            m_getFileContentTable = getFileContentTable;
            m_isPartialReuse = isPartialReuse;
            m_frontendsEnvironmentRestriction = frontEndController.RegisteredFrontEnds.ToDictionary(frontend => frontend.Name, frontEnd => frontEnd.ShouldRestrictBuildParameters);
            m_frontEndController = frontEndController;
            GetTimerUpdatePeriod = timerUpdatePeriod;
            Layout = configuration.Layout;
            m_directoryTranslator = directoryTranslator;

            if (ShouldUseSpecCache(configuration))
            {
                m_specCache = new FileCombiner(
                    loggingContext,
                    Path.Combine(configuration.Layout.EngineCacheDirectory.ToString(PathTable), SpecCacheFileName),
                    FileCombiner.FileCombinerUsage.SpecFileCache,
                    configuration.FrontEnd.LogStatistics);
            }

            m_allBuildParameters = new ConcurrentDictionary<string, TrackedValue>(OperatingSystemHelper.EnvVarComparer);

            foreach (var kvp in PopulateFromEnvironmentAndApplyOverrides(loggingContext, startupConfiguration.Properties).ToDictionary())
            {
                m_allBuildParameters.TryAdd(kvp.Key, TrackedValue.CreateUnused(kvp.Value));
            }
            
            m_localDiskContentStore = new LocalDiskContentStore(
                loggingContext, 
                PathTable, 
                m_getFileContentTable(), 
                m_inputTracker.FileChangeTracker, 
                directoryTranslator,
                vfsCasRoot: configuration.Cache.VfsCasRoot,
                allowReuseOfWeakIdenityForSourceFiles: configuration.Cache.AllowReuseOfWeakIdenityForSourceFiles);

            m_localDiskContentStoreConcurrencyLimiter = ActionBlockSlim.CreateWithAsyncAction<MaterializeFileRequest>(
                Environment.ProcessorCount,
                async request =>
                {
                    var requestCompletionSource = request.CompletionSource;
                    
                    try
                    {
                        var materializeResult = await m_localDiskContentStore.TryMaterializeAsync(
                            request.Cache,
                            request.FileRealizationModes,
                            request.Path,
                            request.ContentHash,
                            trackPath: request.TrackPath,
                            recordPathInFileContentTable: request.RecordPathInFileContentTable);

                        requestCompletionSource.SetResult(materializeResult);
                    }
                    catch (TaskCanceledException)
                    {
                        requestCompletionSource.SetCanceled();
                    }
                    catch (Exception e)
                    {
                        requestCompletionSource.SetException(e);
                    }
                });
            
            m_reparsePointResolver = new ReparsePointResolver(pathTable, m_directoryTranslator);
        }

        /// <inheritdoc/>
        public override AbsolutePath Translate(AbsolutePath path)
        {
            return m_directoryTranslator?.Translate(path, PathTable) ?? path;
        }

        private bool ShouldUseSpecCache(IConfiguration configuration)
        {
            var sourceDirectory = configuration.Layout.SourceDirectory.ToString(PathTable);
            Contract.Assume(
                Path.IsPathRooted(sourceDirectory) && !string.IsNullOrWhiteSpace(sourceDirectory),
                "Config file path should be absolute");
            char driveLetter = sourceDirectory[0];
            var sourceDiskHasSeekPenalty = (char.IsLetter(driveLetter) && driveLetter > 64 && driveLetter < 123)
                ? FileUtilities.DoesLogicalDriveHaveSeekPenalty(driveLetter)
                : false;

            bool createSpecCache = false;

            // the spec cache may be dynamic, enabled, or disabled.
            if (configuration.Cache.CacheSpecs == SpecCachingOption.Auto)
            {
                // If the setting is unspecified, the value is dynamic. Only disable if the disk definitely has no
                // seek penalty. Assume there is one if the property cannot be queried.
                if (sourceDiskHasSeekPenalty.HasValue && sourceDiskHasSeekPenalty.Value == false)
                {
                    Logger.Log.SpecCacheDisabledForNoSeekPenalty(m_loggingContext);
                    createSpecCache = false;
                }
                else
                {
                    createSpecCache = true;
                }
            }
            else if (configuration.Cache.CacheSpecs == SpecCachingOption.Enabled)
            {
                createSpecCache = true;
            }

            int parserConcurrency;
            if (int.TryParse(EngineEnvironmentSettings.ParserIOConcurrency, out parserConcurrency))
            {
                ParserConcurrency = parserConcurrency;
            }
            else
            {
                if (sourceDiskHasSeekPenalty ?? false)
                {
                    ParserConcurrency = Environment.ProcessorCount;
                }
            }

            return createSpecCache;
        }

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath, IReadOnlyList<(string, FileAttributes)> members) => TrackDirectoryInternal(directoryPath, members);

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath) => TrackDirectoryInternal(directoryPath, null);

        /// <summary>
        /// This function is only used by Nuget package download which is going to be replaced soon by not using 1-phase cache lookup anymore.
        /// </summary>
        public override Task<Possible<ContentMaterializationResult, Failure>> TryMaterializeContentAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            ContentHash contentHash,
            bool trackPath = true,
            bool recordPathInFileContentTable = true)
        {
            var request = new MaterializeFileRequest(
                cache,
                fileRealizationModes,
                path,
                contentHash,
                trackPath,
                recordPathInFileContentTable);
            m_localDiskContentStoreConcurrencyLimiter.Post(request);

            return request.CompletionSource.Task;
        }

        /// <inheritdoc/>
        public override async Task<ContentHash> GetFileContentHashAsync(string path, bool trackFile = true, HashType hashType = HashType.Unknown)
        {
            // If the tracker knows about this file, then we already have the hash
            ContentHash hash;
            if (trackFile)
            {
                if (m_inputTracker.TryGetHashForUnchangedFile(path, out hash))
                {
                    return hash;
                }
            }

            using (
                var fs = FileUtilities.CreateFileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Delete | FileShare.Read,
                    FileOptions.SequentialScan))
            {
                // Otherwise, check if the file content table already knows about this
                var fileContentTable = m_getFileContentTable();

                VersionedFileIdentityAndContentInfo? maybeKnownIdentityAndHash = fileContentTable.TryGetKnownContentHash(fs);

                if (maybeKnownIdentityAndHash?.FileContentInfo.MatchesHashType(hashType) ?? false)
                {
                    return maybeKnownIdentityAndHash.Value.FileContentInfo.Hash;
                }

                // Finally, if all the above failed, compute the hash and record it for next time
                hash = await ContentHashingUtilities.HashContentStreamAsync(fs, hashType);
                maybeKnownIdentityAndHash = fileContentTable.RecordContentHash(fs, hash);

                m_specCache?.AddFile(fs, maybeKnownIdentityAndHash.Value.FileContentInfo.Hash, path);
                m_inputTracker.RegisterFileAccess(fs.SafeFileHandle, path, maybeKnownIdentityAndHash.Value);

                return hash;
            }
        }

        /// <inheritdoc/>
        public override bool IsEngineStatePartiallyReloaded()
        {
            return m_isPartialReuse;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture")]
        private void TrackDirectoryInternal(string directoryPath, IReadOnlyList<(string, FileAttributes)> members)
        {
            IsAnyDirectoryEnumerated = true;
            bool succeed = members != null
                ? m_inputTracker.TrackDirectory(directoryPath, members)
                : m_inputTracker.TrackDirectory(directoryPath);

            if (!succeed)
            {
                throw new BuildXLException($"The directory could not be tracked: {directoryPath}");
            }
        }

        /// <inheritdoc />
        public override bool TryGetFrontEndFile(AbsolutePath path, string frontEnd, out Stream stream)
        {
            var physicalPath = path.ToString(PathTable);

            return TryRetrieveAndTrackFile(physicalPath, out stream);
        }

        /// <inheritdoc />
        public override Task<Possible<FileContent, RecoverableExceptionFailure>> GetFileContentAsync(AbsolutePath path)
        {
            var physicalPath = path.ToString(PathTable);

            return RetrieveAndTrackFileAsync(physicalPath);
        }

        /// <inheritdoc />
        public override Possible<string, RecoverableExceptionFailure> GetFileContentSynchronous(AbsolutePath path)
        {
            // There is a big problem with the logic in this file.
            // It is not using the IFileSystem to access the file because that doesn't have any way to get file handles,
            // so there wouldn't be a way to synchroneously track files for graph caching without massive refactorings.
            // Therefore any code dpeendent on this can't use the memory filesystem of unittests.

            try
            {
                var physicalPath = path.ToString(PathTable, PathFormat.HostOs);
                using (
                    FileStream fs = FileUtilities.CreateFileStream(
                        physicalPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Delete | FileShare.Read))
                using (var reader = new BinaryReader(fs))
                {

                    var bytes = reader.ReadBytes((int)fs.Length);
                    var contentHash = ContentHashingUtilities.HashBytes(bytes);

                    m_inputTracker.RegisterAccessAndTrackFile(fs.SafeFileHandle, physicalPath, contentHash);

                    return Encoding.UTF8.GetString(bytes); // File.ReadAllText defaults to UTF8 too.
                }
            }
            catch (Exception e)
            {
                return new RecoverableExceptionFailure(new BuildXLException(e.Message, e));
            }
        }

        /// <inheritdoc />
        public override bool FileExists(AbsolutePath path)
        {
            var physicalPath = path.ToString(PathTable);
            return m_inputTracker.ProbeFileOrDirectoryExistence(physicalPath) == PathExistence.ExistsAsFile;
        }

        /// <inheritdoc />
        public override bool DirectoryExists(AbsolutePath path)
        {
            var physicalPath = path.ToString(PathTable);
            return m_inputTracker.ProbeFileOrDirectoryExistence(physicalPath) == PathExistence.ExistsAsDirectory;
        }

        /// <inheritdoc />
        public override void RecordFrontEndFile(AbsolutePath path, string frontEnd)
        {
            m_inputTracker.RegisterFileAccess(path, PathTable);
        }

        /// <inheritdoc />
        public override bool TryGetBuildParameter(string name, string frontEnd, out string value, LocationData? locationData = null)
        {
            bool success;

            // Build parameters are restricted depending on how the frontend is configured
            var result = m_frontendsEnvironmentRestriction.TryGetValue(frontEnd, out bool restrictBuildParameters);
            Contract.Assert(result, $"Frontend {frontEnd} should be registered. Registered front ends are: {string.Join(", ", m_frontendsEnvironmentRestriction.Keys)}");

            if (!restrictBuildParameters)
            {
                // Uses of environment variable can be get-value or has-variable, and both uses must be tracked.
                var trackedValue = m_allBuildParameters.GetOrAdd(name, key => TrackedValue.CreateUsed(null, locationData ?? LocationData.Invalid));
                trackedValue.NotifyUsed(locationData);
                value = trackedValue.Value;
                success = trackedValue.Value != null;
            }
            else
            {
                Contract.Assume(
                    m_visibleBuildParameters != null,
                    "RestrictBuildParameters must have been called after parsing the configuration was final.");

                success = TryGetAndUse(m_visibleBuildParameters, name, out value, locationData);
            }

            return success;
        }

        /// <summary>
        /// Returns the list of mount names available in the current package
        /// </summary>
        public override IEnumerable<string> GetMountNames(string frontEnd, ModuleId moduleId)
        {
            return m_mountsTable.GetMountNames(moduleId);
        }

        /// <summary>
        /// Gets the mount from the engine for the given moduleId
        /// </summary>
        public override TryGetMountResult TryGetMount(string name, string frontEnd, ModuleId moduleId, out IMount mount)
        {
            TryGetMountResult result = m_mountsTable.TryGetMount(name, moduleId, out mount);

            switch (result)
            {
                case TryGetMountResult.NameNotFound:
                    m_usedMounts.TryAdd(name, null);
                    break;
                case TryGetMountResult.Success:
                    m_usedMounts.TryAdd(name, mount);
                    break;
            }

            return result;
        }

        /// <inheritdoc/>
        public override void AddResolvedModuleDefinedMount(IMount mount, LocationData? mountLocation = null)
        {
            m_mountsTable.AddResolvedModuleDefinedMount(mount, mountLocation);
        }

        /// <inheritdoc/>
        public override bool CompleteMountInitialization()
        {
            var success = m_mountsTable.CompleteInitialization();

            if (!success)
            {
                Contract.Assume(m_loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.CompleteInitialization()");
            }

            return success;
        }

        /// <inheritdoc />
        public override void RestrictBuildParameters(IEnumerable<string> buildParameterNames)
        {
            var dictionary = new Dictionary<string, TrackedValue>(OperatingSystemHelper.EnvVarComparer);
            foreach (var buildParameterName in buildParameterNames)
            {
                if (!dictionary.ContainsKey(buildParameterName))
                {
                    TrackedValue trackedValue;

                    // undefined values are tracked with a null value.
                    var value = !m_allBuildParameters.TryGetValue(buildParameterName, out trackedValue) ? null : trackedValue.Value;

                    // Mark variable as used if it was used in the configuration
                    dictionary.Add(buildParameterName, TrackedValue.CreateUnused(value));
                }
            }

            m_visibleBuildParameters = dictionary;
        }

        #region FileSystem Tracking

        private async Task<Possible<FileContent, RecoverableExceptionFailure>> RetrieveAndTrackFileAsync(string path)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            var result = await TryRetrieveAndTrackFileCore(
                path,
                str =>
                {
                    return FileContent.ReadFromAsync(str);
                });

            return result;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposal is not needed for memory stream")]
        private bool TryRetrieveAndTrackFile(string path, out Stream stream)
        {
            var result = TryRetrieveAndTrackFileCore(
                path,
                str =>
                {
                    var resultStream = str is FileStream ? new MemoryStream(GetBytes(str)) : str;
                    return Task.FromResult(resultStream);
                }).GetAwaiter().GetResult();

            stream = result.Succeeded ? result.Result : null;
            return stream != null;
        }

        /// <summary>
        /// Handles retrieving and tracking files to be used by the parser
        /// </summary>
        private async Task<Possible<T, RecoverableExceptionFailure>> TryRetrieveAndTrackFileCore<T>(string path, Func<Stream, Task<T>> convertStream)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            Stream stream = null;

            // The general principle of this method is that the file is always registered with the InputTracker
            // immediately before returning a stream for that file

            // Checking the specCache for the file
            if (TryReadFromSpecCache(path, out stream))
            {
                return await convertStream(stream);
            }

            // We couldn't establish the file hash (and cached contents) given prior work from a journal-scan / up-to-date check.
            // Now we have to actually open the file.
            try
            {
                using (
                    FileStream fs = FileUtilities.CreateFileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Delete | FileShare.Read,
                        // SequentialScan has the side effect that it will flush the file from the disk cache. This is ok
                        // in this scenario because of the spec file cache.
                        FileOptions.None))
                {
                    FileContentTable fileContentTable = m_getFileContentTable();
                    VersionedFileIdentityAndContentInfo? maybeKnownIdentityAndHash = fileContentTable.TryGetKnownContentHash(fs);

                    if (m_specCache != null)
                    {
                        // Case #2 - The file may have changed. Only look it up in the spec cache if the FileContentTable
                        // knew the hash. Otherwise we'll end up reading the entire file to get the hash anyway, at which
                        // point retrieving it from the spec cache doesn't save any work.
                        if (maybeKnownIdentityAndHash.HasValue)
                        {
                            stream = m_specCache.RequestFile(path, maybeKnownIdentityAndHash.Value.FileContentInfo.Hash);
                            if (stream != null)
                            {
                                m_inputTracker.RegisterFileAccess(fs.SafeFileHandle, path, maybeKnownIdentityAndHash.Value);
                                return await convertStream(stream);
                            }
                        }
                    }

                    if (!maybeKnownIdentityAndHash.HasValue)
                    {
                        // Case #3 - The file was not in the specCache or we must read the file in order to determine its hash.
                        // Read it from disk and register it with the spec cache for the next build
                        ContentHash hash = await ContentHashingUtilities.HashContentStreamAsync(fs);
                        maybeKnownIdentityAndHash = fileContentTable.RecordContentHash(fs, hash);
                    }

                    fs.Position = 0;
                    m_specCache?.AddFile(fs, maybeKnownIdentityAndHash.Value.FileContentInfo.Hash, path);
                    m_inputTracker.RegisterFileAccess(fs.SafeFileHandle, path, maybeKnownIdentityAndHash.Value);

                    // Change this
                    fs.Position = 0;
                    return await convertStream(fs);
                }
            }
            catch (BuildXLException e) when (e.InnerException is IOException || e.InnerException is FileContent.FileContentException)
            {
                return new RecoverableExceptionFailure(e);
            }
        }

        private bool TryReadFromSpecCache(string path, out Stream stream)
        {
            stream = null;
            if (m_specCache != null)
            {
                ContentHash hash;
                if (m_inputTracker.TryGetHashForUnchangedFile(path, out hash))
                {
                    // Case #1 - The InputTracker knows that the file is unchanged and its hash is known.
                    stream = m_specCache.RequestFile(path, hash);
                    if (stream != null)
                    {
                        try
                        {
                            m_inputTracker.RegisterAccessToTrackedFile(path, hash);
                            return true;
                        }
                        catch (BuildXLException)
                        {
                            // this happens if the file changed after the initial journal scan
                            stream.Dispose();
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private static byte[] GetBytes(Stream s)
        {
            long initialPosition = s.Position;
            s.Position = 0;
            byte[] bytes = new byte[s.Length];
            int remaining = bytes.Length;
            int position = 0;
            while (remaining > 0)
            {
                var read = s.Read(bytes, position, remaining);
                remaining -= read;
                position += read;
            }

            s.Position = initialPosition;

            return bytes;
        }

        #endregion

        #region Environment value tracking

        /// <inheritdoc />
        public override void FinishTrackingBuildParameters()
        {
            m_finishedBuildParameterTracking = true;
            m_frontEndController.CompleteCredentialScanner();
            Logger.Log.EnvironmentVariablesImpactingBuild(m_loggingContext, ComputeEnvVarsForLogging());
            Logger.Log.MountsImpactingBuild(m_loggingContext, EffectiveMounts.Create(this));
        }

        private EffectiveEnvironmentVariables ComputeEnvVarsForLogging()
        {
            EffectiveEnvironmentVariables effectiveEnvVars = default;
            (effectiveEnvVars.UsedVariables,    effectiveEnvVars.UsedCount)     = GetVariablesData(ComputeEnvironmentVariablesImpactingBuild());
            (effectiveEnvVars.UnusedVariables,  effectiveEnvVars.UnusedCount)   = GetVariablesData(ComputeUnusedAllowedEnvironmentVariables());
            return effectiveEnvVars;
        }

        private (string, int) GetVariablesData(IReadOnlyDictionary<string, TrackedValue> environmentVariables)
        {
            var count = 0;
            using var pooledStringBuilder = Pools.StringBuilderPool.GetInstance();
            var stringBuilder = pooledStringBuilder.Instance;
            foreach (var environmentVariable in environmentVariables)
            {
                if (count != 0)
                {
                    stringBuilder.AppendLine();
                }

                var value = environmentVariable.Value.Value;
                if (m_frontEndController.CredentialScanResult.EnvVarsWithDetections.Contains(environmentVariable.Key))
                {
                    value = "*** <credential detected>";
                }

                stringBuilder.AppendFormat("{0}={1}", environmentVariable.Key, value);
                if (environmentVariable.Value.FirstLocation.IsValid)
                {
                    stringBuilder.AppendFormat(" [{0}]", environmentVariable.Value.FirstLocation.ToString(PathTable));
                }

                count++;
            }

            return (stringBuilder.ToString(), count);
        }

        /// <summary>
        /// Populates environment variables from the current environment and applies overrides.
        /// </summary>
        internal static IBuildParameters PopulateFromEnvironmentAndApplyOverrides(LoggingContext loggingContext, IReadOnlyDictionary<string, string> overrideVariables)
        {
            return BuildParameters
                .GetFactory((key, existingValue, ignoredValue) =>  ProcessPipExecutor.PipEnvironment.ReportDuplicateVariable(loggingContext, key, existingValue, ignoredValue))
                .PopulateFromEnvironment()
                .Override(overrideVariables);
        }

        private bool TryGetAndUse(IReadOnlyDictionary<string, TrackedValue> parameters, string name, out string value, LocationData? locationData = null)
        {
            Contract.Assert(!m_finishedBuildParameterTracking, "Environment variables cannot be used after FinishTracking is called.");

            TrackedValue trackedValue;
            if (parameters.TryGetValue(name, out trackedValue))
            {
                trackedValue.NotifyUsed(locationData);
                value = trackedValue.Value;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Computes the environment variables which are allowed based on configuration settings but are unused.
        /// </summary>
        internal IReadOnlyDictionary<string, TrackedValue> ComputeUnusedAllowedEnvironmentVariables()
        {
            Contract.Assume(m_visibleBuildParameters != null, "Environment variables must first be restricted");
            Contract.Assume(m_finishedBuildParameterTracking, "Tracking must be finished to access used state of environment variables.");

            var unused = new Dictionary<string, TrackedValue>(OperatingSystemHelper.EnvVarComparer);

            bool isUsedByConfig(string name) => m_allBuildParameters.TryGetValue(name, out var value) && value.Used;

            foreach (var kvp in m_visibleBuildParameters)
            {
                if (!kvp.Value.Used && !isUsedByConfig(kvp.Key))
                {
                    unused.Add(kvp.Key, kvp.Value);
                }
            }

            return unused;
        }

        /// <summary>
        /// Computes the environment variables that impact the build
        /// </summary>
        /// <param name="excludeUnused">indicates whether unused environment variables should be excluded</param>
        /// <remarks>
        /// This includes:
        /// 1. Variables that may be consumed by frontends
        /// 2. The set of variables that were consumed while parsing configs, including null for environment variables
        ///     that were unset but checked
        /// </remarks>
        internal IReadOnlyDictionary<string, TrackedValue> ComputeEnvironmentVariablesImpactingBuild(bool excludeUnused = true)
        {
            Contract.Assume(m_visibleBuildParameters != null, "Build parameters must first be restricted");

            if (excludeUnused)
            {
                Contract.Assume(m_finishedBuildParameterTracking, "Tracking must be finished to access used state of environment variables.");
            }

            var impactingBuild = new Dictionary<string, TrackedValue>(OperatingSystemHelper.EnvVarComparer);

            // Add the ones used by modules and specs
            foreach (var kvp in m_visibleBuildParameters)
            {
                if (excludeUnused && !kvp.Value.Used)
                {
                    continue;
                }

                impactingBuild[kvp.Key] = kvp.Value;
            }

            // Add the ones used by configuration.
            foreach (var kvp in m_allBuildParameters)
            {
                if (!kvp.Value.Used)
                {
                    continue;
                }

                impactingBuild[kvp.Key] = kvp.Value;
            }

            return impactingBuild;
        }

        /// <summary>
        /// Computes effectively used mounts.
        /// </summary>
        /// <remarks>
        /// This method does not consider module defined mounts to be effective mounts. Effective mounts
        /// are used for computing the graph fingerprint for caching purposes, and module-defined mounts
        /// are already implicitly included in that fingerprint by virtue for tracking module configuration file
        /// content environment variables
        /// </remarks>
        internal IReadOnlyDictionary<string, IMount> ComputeEffectiveMounts()
        {
            Contract.Assume(m_finishedBuildParameterTracking, "Tracking must be finished to compute effectively used mounts.");

            // If a referenced mount is not found, the referenced name is made part of m_usedMounts but the mount value will be null
            List<KeyValuePair<string, IMount>> effectiveMounts = m_usedMounts.Where(pair => pair.Value == null || !m_mountsTable.IsModuleDefinedMount(pair.Value)).ToList();

            var result = new Dictionary<string, IMount>(effectiveMounts.Count, StringComparer.OrdinalIgnoreCase);
            foreach(KeyValuePair<string, IMount> effectiveMountPair in effectiveMounts)
            {
                result.Add(effectiveMountPair.Key, effectiveMountPair.Value);
            }

            return result;
        }

        /// <summary>
        /// Helper for unittesting
        /// </summary>
        public IReadOnlyDictionary<string, string> GetAllEnvironmentVariables()
        {
            return m_allBuildParameters.ToDictionary(kv => kv.Key, kv => kv.Value.Value, OperatingSystemHelper.EnvVarComparer);
        }

        internal sealed class TrackedValue
        {
            /// <summary>
            /// Tracks whether the environment variable is used.
            /// </summary>
            public bool Used => Volatile.Read(ref m_used) == 1;

            /// <summary>
            /// Gets the first used location.
            /// </summary>
            public LocationData FirstLocation => m_firstUsedLocation;

            /// <summary>
            /// The environment variable value
            /// </summary>
            public readonly string Value;

            private int m_used;
            private LocationData m_firstUsedLocation;

            /// <summary>
            /// Class constructor
            /// </summary>
            private TrackedValue(string value, bool used, LocationData firstUsedLocation)
            {
                Value = value;
                m_used = used ? 1 : 0;
                m_firstUsedLocation = firstUsedLocation;
            }

            /// <summary>
            /// Creates unused value.
            /// </summary>
            public static TrackedValue CreateUnused(string value) => new (value, false, LocationData.Invalid);

            /// <summary>
            /// Creates used value.
            /// </summary>
            public static TrackedValue CreateUsed(string value, LocationData locationData) => new (value, true, locationData);

            /// <summary>
            /// Indicates that the value is used
            /// </summary>
            public void NotifyUsed(LocationData? location)
            {
                int originalUsed = Interlocked.CompareExchange(ref m_used, 1, 0);
                if (originalUsed == 0 && location.HasValue && location.Value.IsValid)
                {
                    m_firstUsedLocation = location.Value;
                }
            }
        }
        #endregion

        /// <nodoc/>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = nameof(m_specCache), Justification = "ReleaseSpecCacheMemory disposes the spec cache")]
        public void Dispose()
        {
            ReleaseSpecCacheMemory();
        }

        /// <inheritdoc />
        public override void ReleaseSpecCacheMemory()
        {
            m_specCache?.Dispose();
            m_specCache = null;
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetUnchangedFiles()
        {
            return m_inputTracker.UnchangedFiles;
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetChangedFiles()
        {
            return m_inputTracker.ChangedFiles;
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> EnumerateEntries(AbsolutePath path, string pattern, bool recursive, bool directories)
        {
            var entries = new List<AbsolutePath>();
            
            var result = FileUtilities.EnumerateDirectoryEntries(
                path.ToString(PathTable),
                recursive,
                pattern,
                (currentDirectory, name, attr) =>
                {
                    if ((attr & FileAttributes.Directory) != 0 == directories)
                    {
                        var fullName = Path.Combine(currentDirectory, name);
                        var fullNamePath = AbsolutePath.Create(PathTable, fullName);
                        Contract.Assert(fullNamePath.IsValid);

                        entries.Add(fullNamePath);
                    }
                });

            // If the result indicates that the enumeration succeeded or the directory does not exist, then the result is considered success.
            // In particular, if the globed directory does not exist, then we want to return the empty file, and track for the anti-dependency.
            if (
                !(result.Status == EnumerateDirectoryStatus.Success ||
                  result.Status == EnumerateDirectoryStatus.SearchDirectoryNotFound))
            {
                throw new BuildXLException(I($"Error enumerating path '{path.ToString(PathTable)}'."), result.CreateExceptionForError());
            }

            var success = m_inputTracker.TrackDirectory(path.ToString(PathTable));

            Contract.Assert(success);

            return entries;
        }

        /// <inheritdoc/>
        public override IEnumerable<AbsolutePath> GetAllIntermediateReparsePoints(AbsolutePath path) 
            => m_reparsePointResolver.GetAllReparsePointsInChains(path);

        private readonly struct MaterializeFileRequest
        {
            public IArtifactContentCache Cache { get; }
            public FileRealizationMode FileRealizationModes { get; }
            public AbsolutePath Path { get; }
            public ContentHash ContentHash { get; }
            public bool TrackPath { get; }
            public bool RecordPathInFileContentTable { get; }

            public TaskSourceSlim<Possible<ContentMaterializationResult, Failure>> CompletionSource { get; }

            public MaterializeFileRequest(
                IArtifactContentCache cache,
                FileRealizationMode fileRealizationModes,
                AbsolutePath path,
                ContentHash contentHash,
                bool trackPath,
                bool recordPathInFileContentTable)
            {
                Cache = cache;
                FileRealizationModes = fileRealizationModes;
                Path = path;
                ContentHash = contentHash;
                TrackPath = trackPath;
                RecordPathInFileContentTable = recordPathInFileContentTable;
                CompletionSource = TaskSourceSlim.Create<Possible<ContentMaterializationResult, Failure>>();
            }
        }
    }
}
