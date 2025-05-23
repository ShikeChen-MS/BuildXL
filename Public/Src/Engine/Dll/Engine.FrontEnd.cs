// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

using static BuildXL.Utilities.Core.FormattableStringEx;
using Logger = BuildXL.Engine.Tracing.Logger;
using Pure = System.Diagnostics.Contracts.PureAttribute;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Scheduler.Fingerprints;

namespace BuildXL.Engine
{
    /// <summary>
    /// Engine stuff that should eventually be moved to FrontEnd.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public sealed partial class BuildXLEngine
    {
        private bool ConstructAndEvaluateGraph(
            LoggingContext loggingContext,
            FrontEndEngineAbstraction frontEndEngineAbstration,
            CacheInitializationTask engineCacheTask,
            MountsTable mountsTable,
            EvaluationFilter evaluationFilter,
            [AllowNull] GraphReuseResult reuseResult,
            [AllowNull] PipSpecificPropertiesConfig pipSpecificPropertiesConfig,
            out PipGraph pipGraph)
        {
            Contract.Requires(frontEndEngineAbstration != null);
            Contract.Requires(engineCacheTask != null);
            Contract.Requires(mountsTable != null);

            pipGraph = null;
            IPipGraphBuilder pipGraphBuilder = null;

            // Observe the mount table is not finalized here since there might be extra mounts coming
            // from DScript modules
            AddConfigurationMounts(mountsTable);

            IDictionary<ModuleId, MountsTable> moduleMountsTableMap;
            if (!mountsTable.PopulateModuleMounts(Configuration.ModulePolicies.Values, out moduleMountsTableMap))
            {
                Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.PopulateModuleMounts()");
                return false;
            }

            if (Configuration.Engine.Phase.HasFlag(EnginePhases.Schedule))
            {
                pipGraphBuilder = CreatePipGraphBuilder(loggingContext, mountsTable, reuseResult, pipSpecificPropertiesConfig: pipSpecificPropertiesConfig);
            }

            // Have to do some horrible magic here to get to a proper Task<T> with the BuildXL cache since
            // someone updated the engine cache to be an await style pattern, and there is no way to get to the EngineCache
            // If the cache was fast to startup, but perhaps blocked itself on first access we wouldn't have to do all these hoops.
            Func<Task<Possible<EngineCache>>> getBuildCacheTask =
                async () =>
                {
                    return (await engineCacheTask).Then(engineCache => engineCache.CreateCacheForContext());
                };

            if (!FrontEndController.PopulateGraph(
                getBuildCacheTask(),
                pipGraphBuilder,
                frontEndEngineAbstration,
                evaluationFilter,
                Configuration,
                m_initialCommandLineConfiguration.Startup))
            {
                FrontEndController.CompleteCredentialScanner();
                LogFrontEndStats(loggingContext);

                Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after FrontEndController.PopulateGraph()");
                return false;
            }

            // Build should gracefully fail if credentials are detected in the environment variables.
            bool secretDetected = FrontEndController.CredentialScanResult.CredentialDetected;
            LogFrontEndStats(loggingContext);

            // Pip graph must become immutable now that evaluation is done (required to construct a scheduler).
            return (pipGraphBuilder == null || (pipGraph = pipGraphBuilder.Build()) != null) && !secretDetected;
        }

        private void AddConfigurationMounts(MountsTable mountsTable)
        {
            // Add configuration mounts
            foreach (var mount in Configuration.Mounts)
            {
                mountsTable.AddResolvedMount(mount, new LocationData(Configuration.Layout.PrimaryConfigFile, 0, 0));
            }
        }

        private bool CompleteInitialization(LoggingContext loggingContext, MountsTable mountsTable)
        {
            if (!mountsTable.CompleteInitialization())
            {
                Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.CompleteInitialization()");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates an <see cref="IPipGraphBuilder"/>.
        ///
        /// If configured to use graph patching (<see cref="FrontEndConfigurationExtensions.UseGraphPatching"/>) and
        /// previous pip graph was reloaded and is available for partial reuse (<see cref="GraphReuseResult.IsPartialReuse"/>),
        /// it creates a builder that supports graph patching (<see cref="PatchablePipGraph"/>).
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose EngineSchedule.CreateEmptyPipTable",
            Justification = "Caller is responsible for disposing pipGraph")]
        private IPipGraphBuilder CreatePipGraphBuilder(
            LoggingContext loggingContext,
            MountsTable mountsTable,
            [AllowNull] GraphReuseResult reuseResult,
            [AllowNull] PipSpecificPropertiesConfig pipSpecificPropertiesConfig)
        {
            var searchPathToolsHash = new Scheduler.DirectoryMembershipFingerprinterRuleSet(Configuration, Context.StringTable).ComputeSearchPathToolsHash();
            ContentHash? observationReclassificationRulesHash = ObservationReclassifier.ComputeObservationReclassificationRulesHash(Configuration);
            var builder = new PipGraph.Builder(
                EngineSchedule.CreateEmptyPipTable(Context),
                Context,
                BuildXL.Pips.Tracing.Logger.Log,
                loggingContext,
                Configuration,
                mountsTable.MountPathExpander,
                fingerprintSalt: Configuration.Cache.CacheSalt,
                searchPathToolsHash: searchPathToolsHash,
                observationReclassificationRulesHash: observationReclassificationRulesHash,
                pipSpecificPropertiesConfig: pipSpecificPropertiesConfig);

            PatchablePipGraph patchableGraph = null;
            if (Configuration.FrontEnd.UseGraphPatching() && reuseResult?.IsPartialReuse == true)
            {
                Logger.Log.UsingPatchableGraphBuilder(loggingContext);
                patchableGraph = new PatchablePipGraph(
                    oldPipGraph: reuseResult.PipGraph.DirectedGraph,
                    oldPipTable: reuseResult.PipGraph.PipTable,
                    graphBuilder: builder,
                    maxDegreeOfParallelism: Configuration.FrontEnd.MaxFrontEndConcurrency());
            }

            return (IPipGraphBuilder)patchableGraph ?? builder;
        }

        /// <summary>
        /// Check if pip graph can be reused.
        ///
        /// There are 3 opportunities to determine a graph match. The applicability of each depends on the distributed build roles.
        ///   (1) from engine cache,
        ///   (2) from content cache, and
        ///   (3) from orchestrator node (if running on a worker node in a distributed build)
        /// </summary>
        private GraphCacheCheckStatistics CheckGraphCacheReuse(
            LoggingContext outerLoggingContext,
            int maxDegreeOfParallelism,
            GraphFingerprint graphFingerprint,
            IReadOnlyDictionary<string, string> properties,
            CacheInitializationTask cacheInitializationTask,
            JournalState journalState,
            out EngineSerializer serializer,
            out InputTracker.InputChanges inputChanges)
        {
            serializer = CreateEngineSerializer(outerLoggingContext);
            inputChanges = null;
            var cacheGraphStats = default(GraphCacheCheckStatistics);

            using (var timeBlock = TimedBlock<EmptyStruct, GraphCacheCheckStatistics>.Start(
                outerLoggingContext,
                Statistics.GraphCacheReuseCheck,
                (context, emptyStruct) => Logger.Log.CheckingForPipGraphReuseStart(context),
                default(EmptyStruct),
                (loggingContext, stats) =>
                {
                    Logger.Log.CheckingForPipGraphReuseComplete(loggingContext, stats);

                    // On misses we want to give the user a message for why there was a miss
                    if (!stats.WasHit)
                    {
                        Contract.Assume(stats.MissReason != GraphCacheMissReason.NoMiss);
                        Logger.Log.GraphNotReusedDueToChangedInput(loggingContext, stats.MissMessageForConsole, stats.MissDescription);
                    }

                    m_enginePerformanceInfo.GraphCacheCheckDurationMs = stats.ElapsedMilliseconds;
                    m_enginePerformanceInfo.GraphCacheCheckJournalEnabled = stats.JournalEnabled;
                },
                () => cacheGraphStats))
            {
                var loggingContext = timeBlock.LoggingContext;
                var effectiveEnvironmentVariables = FrontEndEngineImplementation.PopulateFromEnvironmentAndApplyOverrides(loggingContext, properties);
                var mainConfigAvailableMounts = MountsTable.CreateAndRegister(loggingContext, Context, Configuration, m_initialCommandLineConfiguration.Startup.Properties);

                AddConfigurationMounts(mainConfigAvailableMounts);
                if (!CompleteInitialization(loggingContext, mainConfigAvailableMounts))
                {
                    return cacheGraphStats;
                }

                cacheGraphStats.JournalEnabled = journalState.IsEnabled;

                // ************************************************************
                // 1. Engine cache check:
                // ************************************************************
                // * Single machine builds
                // Distributed builds rely on the graph being available via the cache for it to be shared between orchestrator
                // and workers. So even if the orchestrator could have had a hit from the engine cache, it must be ignored
                // since the workers would not be able to retrieve it.
                if (!HasExplicitlyLoadedGraph(Configuration.Cache) &&
                    !Configuration.Schedule.ForceUseEngineInfoFromCache &&
                    Configuration.Distribution.BuildRole == DistributedBuildRoles.None)
                {
                    Contract.Assume(
                        graphFingerprint != null,
                        "When looking up a cached graph on a distributed orchestrator or single-machine build, a graph fingerprint must be computed");

                    InputTracker.MatchResult engineCacheMatchResult = CheckIfAvailableInputsToGraphMatchPreviousRun(
                        loggingContext,
                        serializer,
                        graphFingerprint: graphFingerprint,
                        availableEnvironmentVariables: effectiveEnvironmentVariables,
                        mainConfigAvailableMounts: mainConfigAvailableMounts,
                        journalState: journalState,
                        maxDegreeOfParallelism: maxDegreeOfParallelism);
                    cacheGraphStats.ObjectDirectoryHit = engineCacheMatchResult.Matches;
                    cacheGraphStats.ObjectDirectoryMissReason = engineCacheMatchResult.MissType;
                    cacheGraphStats.MissReason = engineCacheMatchResult.MissType;
                    cacheGraphStats.MissDescription = engineCacheMatchResult.FirstMissIdentifier;
                    cacheGraphStats.WasHit = engineCacheMatchResult.Matches;
                    cacheGraphStats.InputFilesChecked = engineCacheMatchResult.FilesChecked;

                    // Checking the engine cache may have used a FileChangeTracker and provided information about
                    // files/ContentHash pairs that were unchanged from the previous run. Hold onto this information as it may
                    // be useful when eventually parsing files.
                    // The FileChangeTracker is now up to date. All changed files have been removed from it. It can be reused
                    // for a future build with the same fingerprint, though it may be tracking extra files, for example if
                    // a spec was removed since the previous build.
                    inputChanges = engineCacheMatchResult.InputChanges;
                }

                var shouldTryContentCache =
                    !cacheGraphStats.WasHit &&
                    Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker &&
                    Configuration.Cache.AllowFetchingCachedGraphFromContentCache &&
                    !HasExplicitlyLoadedGraph(Configuration.Cache) &&
                    (!Configuration.FrontEnd.UseSpecPublicFacadeAndAstWhenAvailable.HasValue ||
                     !Configuration.FrontEnd.UseSpecPublicFacadeAndAstWhenAvailable.Value);

                // ************************************************************
                // 2. Content cache check:
                // ************************************************************
                // * Single machine builds that missed earlier
                // * Distributed orchestrators
                // This is the only valid place for the orchestrator to get a hit since it must be in the cache for the
                // workers to get it.
                if (shouldTryContentCache)
                {
                    // Since an in-place match did not succeed, we need a readied cache to try again.
                    // TODO: This logs an error if it fails. We are assuming some later thing will ensure that, if failed,
                    //       the engine fails overall.
                    Possible<CacheInitializer> possibleCacheInitializerForFallback = cacheInitializationTask.GetAwaiter().GetResult();
                    if (possibleCacheInitializerForFallback.Succeeded)
                    {
                        CacheInitializer cacheInitializerForFallback = possibleCacheInitializerForFallback.Result;
                        using (EngineCache cacheForFallback = cacheInitializerForFallback.CreateCacheForContext())
                        {
                            var cacheGraphProvider = new CachedGraphProvider(
                                loggingContext,
                                Context,
                                cacheForFallback,
                                FileContentTable,
                                maxDegreeOfParallelism);

                            // Module-defined mounts won't be available at this point, but it is safe to only use the main config ones since module defined ones are driven
                            // by the content of module files and environment variables registered through the engine
                            var cachedGraphDescriptor =
                                cacheGraphProvider.TryGetPipGraphCacheDescriptorAsync(graphFingerprint, effectiveEnvironmentVariables, mainConfigAvailableMounts.MountsByName).Result;

                            if (cachedGraphDescriptor == null)
                            {
                                // There was no matching fingerprint in the cache. Record the status for logging before returning.
                                cacheGraphStats.CacheMissReason = GraphCacheMissReason.FingerprintChanged;
                                SetMissReasonIfUnset(ref cacheGraphStats, cacheGraphStats.CacheMissReason);
                                return cacheGraphStats;
                            }

                            var fetchEngineScheduleContent = EngineSchedule.TryFetchFromCacheAsync(
                                loggingContext,
                                Context,
                                cacheForFallback,
                                cachedGraphDescriptor,
                                serializer,
                                FileContentTable,
                                m_tempCleaner).Result;

                            if (!fetchEngineScheduleContent)
                            {
                                cacheGraphStats.CacheMissReason = GraphCacheMissReason.NoPreviousRunToCheck;
                                SetMissReasonIfUnset(ref cacheGraphStats, cacheGraphStats.CacheMissReason);
                                return cacheGraphStats;
                            }

                            // If a distributed orchestrator, take note of the graph fingerprint
                            if (Configuration.Distribution.BuildRole.IsOrchestrator())
                            {
                                Contract.Assert(cachedGraphDescriptor != null);
                                m_orchestratorService.CachedGraphDescriptor = cachedGraphDescriptor;
                            }

                            Logger.Log.FetchedSerializedGraphFromCache(outerLoggingContext);

                            cacheGraphStats.CacheMissReason = GraphCacheMissReason.NoMiss;
                            cacheGraphStats.MissReason = cacheGraphStats.CacheMissReason;
                            cacheGraphStats.WasHit = true;
                        }
                    }
                    else
                    {
                        cacheGraphStats.CacheMissReason = GraphCacheMissReason.CacheFailure;
                        SetMissReasonIfUnset(ref cacheGraphStats, cacheGraphStats.CacheMissReason);
                        return cacheGraphStats;
                    }
                }

                // ************************************************************
                // 3. Query distributed orchestrator
                // ************************************************************
                // * Distributed workers only
                if (Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker)
                {
                    Contract.Assume(
                        graphFingerprint == null,
                        "Distributed workers should request a graph fingerprint from the orchestrator (not compute one locally)");
                    Possible<CacheInitializer> possibleCacheInitializerForWorker = cacheInitializationTask.GetAwaiter().GetResult();
                    Contract.Assume(possibleCacheInitializerForWorker.Succeeded, "Workers must have a valid cache");
                    CacheInitializer cacheInitializerForWorker = possibleCacheInitializerForWorker.Result;

                    using (EngineCache cacheForWorker = cacheInitializerForWorker.CreateCacheForContext())
                    {
                        PipGraphCacheDescriptor schedulerStateDescriptor;
                        if (!m_workerService.TryGetBuildScheduleDescriptor(out schedulerStateDescriptor) ||
                            !EngineSchedule.TryFetchFromCacheAsync(
                                outerLoggingContext,
                                Context,
                                cacheForWorker,
                                schedulerStateDescriptor,
                                serializer,
                                FileContentTable,
                                m_tempCleaner).Result)
                        {
                            cacheGraphStats.CacheMissReason = GraphCacheMissReason.NoFingerprintFromOrchestrator;
                            cacheGraphStats.MissReason = cacheGraphStats.CacheMissReason;
                            return cacheGraphStats;
                        }

                        // Success. Populate the stats
                        cacheGraphStats.WasHit = true;
                        cacheGraphStats.WorkerHit = true;
                        cacheGraphStats.MissReason = GraphCacheMissReason.NoMiss;
                        cacheGraphStats.CacheMissReason = GraphCacheMissReason.NoMiss;
                    }
                }
            }

            return cacheGraphStats;
        }

        /// <summary>
        /// Attempt to reuse the pip graph from a previous run
        /// </summary>
        private GraphReuseResult AttemptToReuseGraph(
            LoggingContext outerLoggingContext,
            int maxDegreeOfParallelism,
            GraphFingerprint graphFingerprint,
            IReadOnlyDictionary<string, string> properties,
            CacheInitializationTask cacheInitializationTask,
            JournalState journalState,
            EngineState engineState)
        {
            GraphCacheCheckStatistics cacheGraphStats = CheckGraphCacheReuse(
                outerLoggingContext,
                maxDegreeOfParallelism,
                graphFingerprint,
                properties,
                cacheInitializationTask,
                journalState,
                out var serializer,
                out var inputChanges);

            // There are 3 cases in which we should reload the graph
            //   - we have a graph cache hit
            //   - the build is configured to reload the graph no matter what
            //   - graph patching is enabled and the reason for cache miss was 'SpecFileChanges'
            var shouldReload =
                cacheGraphStats.WasHit ||
                Configuration.Cache.CachedGraphPathToLoad.IsValid ||
                PartialReloadCondition(Configuration.FrontEnd, cacheGraphStats);

            if (!shouldReload)
            {
                return GraphReuseResult.CreateForNoReuse(inputChanges);
            }

            bool fullReload = !PartialReloadCondition(Configuration.FrontEnd, cacheGraphStats);

            // Now we actually reload the graph
            var reloadStats = default(GraphCacheReloadStatistics);
            using (var tb = TimedBlock<EmptyStruct, GraphCacheReloadStatistics>.Start(
                outerLoggingContext,
                Statistics.GraphCacheReload,
                (context, emptyStruct) =>
                {
                    if (fullReload)
                    {
                        Logger.Log.ReloadingPipGraphStart(context);
                    }
                    else
                    {
                        Logger.Log.PartiallyReloadingEngineState(context);
                    }
                },
                default(EmptyStruct),
                (context, graphCacheCheckStatistics) =>
                {
                    Logger.Log.PartiallyReloadingEngineStateComplete(context, graphCacheCheckStatistics);
                    m_enginePerformanceInfo.GraphReloadDurationMs = graphCacheCheckStatistics.ElapsedMilliseconds;
                },
                () => reloadStats))
            {
                var reuseResult = fullReload
                    ? ReloadEngineSchedule(
                        serializer,
                        cacheInitializationTask,
                        journalState,
                        tb.LoggingContext,
                        engineState,
                        inputChanges,
                        graphFingerprint?.ExactFingerprint.BuildEngineHash.ToString())
                    : ReloadPipGraphOnly(serializer, tb.LoggingContext, engineState, inputChanges);

                // Set telemetry statistics
                reloadStats.SerializedFileSizeBytes = serializer.BytesDeserialized;
                reloadStats.Success = !reuseResult.IsNoReuse;

                return reuseResult;
            }
        }

        private static void SetMissReasonIfUnset(ref GraphCacheCheckStatistics cacheGraphStats, GraphCacheMissReason cacheMissReason)
        {
            if (cacheGraphStats.MissReason == GraphCacheMissReason.NotChecked)
            {
                cacheGraphStats.MissReason = cacheMissReason;
            }
        }

        private static bool PartialReloadCondition(IFrontEndConfiguration frontEndConf, GraphCacheCheckStatistics cacheGraphStats)
        {
            return frontEndConf.ReloadPartialEngineStateWhenPossible() && cacheGraphStats.MissReason == GraphCacheMissReason.SpecFileChanges;
        }

        private GraphReuseResult ReloadPipGraphOnly(
            EngineSerializer serializer,
            LoggingContext loggingContext,
            EngineState engineState,
            InputTracker.InputChanges inputChanges)
        {
            Tuple<PipGraph, EngineContext> t = null;
            try
            {
                t = EngineSchedule.LoadPipGraphAsync(
                    Context,
                    serializer,
                    loggingContext,
                    engineState,
                    m_console).GetAwaiter().GetResult();
            }
            catch (BuildXLException e)
            {
                Logger.Log.FailedReloadPipGraph(loggingContext, e.ToString());
            }

            if (t == null)
            {
                return GraphReuseResult.CreateForNoReuse(inputChanges);
            }

            var newContext = t.Item2;
            if (!ShouldReuseReloadedEngineContextGivenHistoricData(loggingContext, newContext.NextHistoricTableSizes))
            {
                return GraphReuseResult.CreateForNoReuse(inputChanges);
            }

            var newPathTable = newContext.PathTable;
            var pathRemapper = new PathRemapper(Context.PathTable, newPathTable);
            Configuration = new ConfigurationImpl(Configuration, pathRemapper);

            m_initialCommandLineConfiguration = new CommandLineConfiguration(m_initialCommandLineConfiguration, pathRemapper);

            // Invalidate the old context to ensure nothing uses it anymore
            // Update engine state to deserialized state
            Context.Invalidate();
            Context = newContext;

            // Additionally recreate front end controller, because the old one uses the invalidated context.
            //   - to fully initialize the front end, we have to go through all the steps that have already been
            //     executed on the old controller; those steps are (1) InitializeHost, and (2) ParseConfig
            FrontEndController = m_frontEndControllerFactory.Create(Context.PathTable, Context.SymbolTable);
            FrontEndController.InitializeHost(Context.ToFrontEndContext(loggingContext, m_initialCommandLineConfiguration.FrontEnd), m_initialCommandLineConfiguration);

            var configurationEngine = new BasicFrontEndEngineAbstraction(Context.PathTable, Context.FileSystem, m_initialCommandLineConfiguration);
            if (!configurationEngine.TryPopulateWithDefaultMountsTable(loggingContext, Context, m_initialCommandLineConfiguration, m_initialCommandLineConfiguration.Startup.Properties))
            {
                Contract.Assert(loggingContext.ErrorWasLogged);
                return null;
            }

            FrontEndController.ParseConfig(configurationEngine, m_initialCommandLineConfiguration);

            return GraphReuseResult.CreateForPartialReuse(t.Item1, inputChanges);
        }

        private static bool ShouldReuseReloadedEngineContextGivenHistoricData(LoggingContext loggingContext, HistoricTableSizes historicTableSizes)
        {
            Contract.Requires(historicTableSizes != null);
            Contract.Requires(historicTableSizes.Count > 0);

            var gen = historicTableSizes.Count;

            // no historic data --> reuse
            if (gen == 1)
            {
                Logger.Log.EngineContextHeuristicOutcomeReuse(loggingContext, gen, "first generation context is always reused");
                return true;
            }

            var leastRecentStats = historicTableSizes[0];
            var mostRecentStats = historicTableSizes[historicTableSizes.Count - 1];
            var leastRecentTotalSizeInBytes = leastRecentStats.TotalSizeInBytes();
            var mostRecentTotalsizeInBytes = mostRecentStats.TotalSizeInBytes();

            // if the total size of all tables has more than doubled --> don't reuse
            if (mostRecentTotalsizeInBytes > leastRecentTotalSizeInBytes * 2)
            {
                Logger.Log.EngineContextHeuristicOutcomeSkip(
                    loggingContext,
                    gen,
                    I($"total size has more than doubled (oldest size = {leastRecentTotalSizeInBytes} bytes, newest size = {mostRecentTotalsizeInBytes} bytes)"));
                return false;
            }

            Logger.Log.EngineContextHeuristicOutcomeReuse(
                loggingContext,
                gen,
                I($"total size hasn't grown too much (oldest size = {leastRecentTotalSizeInBytes} bytes, newest size = {mostRecentTotalsizeInBytes} bytes)"));
            return true;
        }

        private GraphReuseResult ReloadEngineSchedule(
            EngineSerializer serializer,
            CacheInitializationTask cacheInitializationTask,
            JournalState journalState,
            LoggingContext loggingContext,
            EngineState engineState,
            InputTracker.InputChanges inputChanges,
            string buildEngineFingerprint)
        {
            Tuple<EngineSchedule, EngineContext, IConfiguration> t = null;

            try
            {
                t = EngineSchedule.LoadAsync(
                    Context,
                    serializer,
                    cacheInitializationTask,
                    FileContentTable,
                    journalState,
                    Configuration,
                    loggingContext,
                    m_collector,
                    m_directoryTranslator,
                    engineState,
                    tempCleaner: m_tempCleaner,
                    buildEngineFingerprint,
                    m_pipSpecificPropertiesConfig,
                    m_console).GetAwaiter().GetResult();
            }
            catch (BuildXLException e)
            {
                Logger.Log.FailedReloadPipGraph(loggingContext, e.ToString());
            }

            if (t == null)
            {
                return GraphReuseResult.CreateForNoReuse(inputChanges);
            }

            // Invalidate the old context to ensure nothing uses it anymore
            // Update engine state to deserialized state
            Context.Invalidate();
            Context = t.Item2;

            // Update configuration state to deserialized state
            Configuration = t.Item3;

            // Copy the graph files to the session output
            if (Configuration.Distribution.BuildRole != DistributedBuildRoles.Worker)
            {
                // No need to link these files to the logs directory on workers since they are redundant with what's on the orchestrator
                m_executionLogGraphCopy = TryCreateHardlinksToScheduleFilesInSessionFolder(loggingContext, serializer);
                m_previousInputFilesCopy = TryCreateHardlinksToPreviousInputFilesInSessionFolder(loggingContext, serializer);
            }

            return GraphReuseResult.CreateForFullReuse(t.Item1, inputChanges);
        }
    }

    /// <summary>
    /// Result of graph reuse check.
    /// </summary>
    public sealed class GraphReuseResult
    {
        private readonly PipGraph m_pipGraph;
        private readonly EngineSchedule m_engineSchedule;

        /// <summary>
        /// 'FullReuse' means that no spec changed and everything was successfully reloaded.
        /// When this is true, the entire <see cref="EngineSchedule"/> is available; subsequently,
        /// the engine can use the <see cref="EngineSchedule"/> and skip all the front-end phases.
        /// </summary>
        internal bool IsFullReuse => m_engineSchedule != null;

        /// <summary>
        /// 'PartialReuse' means that some spec files changed and that the previous pip graph and
        /// engine context were successfully reloaded.  When this is true, <see cref="PipGraph"/>
        /// is available, but not <see cref="EngineSchedule"/>.  Subsequently, the engine must
        /// still run the front-end phases, but it should also try to reuse the previous pip
        /// graph and enable graph patching (if so configured).
        /// </summary>
        internal bool IsPartialReuse => m_pipGraph != null;

        /// <summary>
        /// 'NoReuse' means that nothing from the engine cache could be reused.
        /// </summary>
        internal bool IsNoReuse => !IsFullReuse && !IsPartialReuse;

        internal InputTracker.InputChanges InputChanges { get; }

        /// <summary>
        /// May only be called when <see cref="IsFullReuse"/>.
        /// </summary>
        internal EngineSchedule EngineSchedule
        {
            get
            {
                Contract.Requires(IsFullReuse);
                return m_engineSchedule;
            }
        }

        /// <summary>
        /// May only be called when <see cref="IsPartialReuse"/>.
        /// </summary>
        internal PipGraph PipGraph
        {
            get
            {
                Contract.Requires(IsPartialReuse);
                return m_pipGraph;
            }
        }

        /// <summary>
        /// Factory method for the case when nothing can be reused.
        /// Input changes may still optionally be provided.
        /// </summary>
        internal static GraphReuseResult CreateForNoReuse([AllowNull] InputTracker.InputChanges inputChanges)
        {
            return new GraphReuseResult(
                pipGraph: null,
                engineSchedule: null,
                inputChanges: inputChanges);
        }

        /// <summary>
        /// Factory method for the case when everything can be reused.
        /// A non-null engine schedule must be provided; input changes may optionally be provided too.
        /// </summary>
        internal static GraphReuseResult CreateForFullReuse(EngineSchedule engineSchedule, [AllowNull] InputTracker.InputChanges inputChanges)
        {
            Contract.Requires(engineSchedule != null);

            return new GraphReuseResult(
                pipGraph: null,
                engineSchedule: engineSchedule,
                inputChanges: inputChanges);
        }

        /// <summary>
        /// Factory method for the case when pip graph can be reused for graph patching.
        /// A non-null pip graph must be provided; input changes may optionally be provided too.
        /// </summary>
        internal static GraphReuseResult CreateForPartialReuse(PipGraph pipGraph, InputTracker.InputChanges inputChanges)
        {
            Contract.Requires(pipGraph != null);
            Contract.Requires(inputChanges != null);
            Contract.Requires(inputChanges.ChangedPaths.Any());

            return new GraphReuseResult(
                pipGraph: pipGraph,
                engineSchedule: null,
                inputChanges: inputChanges);
        }

        private GraphReuseResult(PipGraph pipGraph, EngineSchedule engineSchedule, InputTracker.InputChanges inputChanges)
        {
            m_pipGraph = pipGraph;
            m_engineSchedule = engineSchedule;
            InputChanges = inputChanges;

            // Calling invariant method explicitely because this is the only way to check it at least once.
            CheckInvariants();
        }

        private void CheckInvariants()
        {
            Contract.Assert(Bool2Int(IsFullReuse) + Bool2Int(IsPartialReuse) + Bool2Int(IsNoReuse) == 1, "Exactly one of IsFullReload, IsPartialReload, and IsNoReload must be true");
            Contract.Assert(IsFullReuse == (m_engineSchedule != null));
            Contract.Assert(IsPartialReuse == (m_pipGraph != null));
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:NoUpstreamCallers", Justification = "It has upstream callers, see method 'Invariant'")]
        private static int Bool2Int(bool b) => b ? 1 : 0;
    }
}