// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Factory;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.PipGraphFragmentGenerator.Tracing;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.PipGraphFragmentGenerator
{
    /// <summary>
    /// Class for generating pip graph fragment.
    /// </summary>
    public static class PipGraphFragmentGenerator
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private static bool TryBuildPipGraphFragment(
            ICommandLineConfiguration commandLineConfig,
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentGeneratorConfig,
            FrontEndContext frontEndContext,
            EngineContext engineContext,
            EvaluationFilter evaluationFilter)
        {
            Contract.Requires(frontEndContext != null);
            Contract.Requires(engineContext != null);
            Contract.Requires(commandLineConfig.Startup.ConfigFile.IsValid);
            Contract.Requires(evaluationFilter != null);

            var pathTable = engineContext.PathTable;
            var loggingContext = frontEndContext.LoggingContext;

            var mutableCommandlineConfig = CompleteCommandLineConfiguration(commandLineConfig);
            BuildXLEngine.ModifyConfigurationForCloudbuild(mutableCommandlineConfig, false, pathTable, loggingContext);
            BuildXLEngine.PopulateLoggingAndLayoutConfiguration(mutableCommandlineConfig, pathTable, bxlExeLocation: null);

            var statistics = new FrontEndStatistics();
            var frontEndControllerFactory = FrontEndControllerFactory.Create(
                mode: FrontEndMode.NormalMode,
                loggingContext: loggingContext,
                configuration: mutableCommandlineConfig,
                collector: null,
                statistics: statistics);

            var controller = frontEndControllerFactory.Create(engineContext.PathTable, engineContext.SymbolTable);
            controller.InitializeHost(frontEndContext, mutableCommandlineConfig);

            FrontEndHostController frontEndHostController = (FrontEndHostController)controller;

            var configurationEngine = new BasicFrontEndEngineAbstraction(engineContext.PathTable, engineContext.FileSystem, mutableCommandlineConfig);
            if (!configurationEngine.TryPopulateWithDefaultMountsTable(loggingContext, engineContext, mutableCommandlineConfig, mutableCommandlineConfig.Startup.Properties))
            {
                // Errors are logged already
                return false;
            }

            var config = controller.ParseConfig(configurationEngine, mutableCommandlineConfig);

            if (config == null)
            {
                return false;
            }

            using (var cache = Task.FromResult<Possible<EngineCache>>(
                new EngineCache(
                    new InMemoryArtifactContentCache(),
                    new EmptyTwoPhaseFingerprintStore())))
            {
                var mountsTable = MountsTable.CreateAndRegister(loggingContext, engineContext, config, mutableCommandlineConfig.Startup.Properties);
                FrontEndEngineAbstraction frontEndEngineAbstraction = new FrontEndEngineImplementation(
                    loggingContext,
                    frontEndContext.PathTable,
                    config,
                    mutableCommandlineConfig.Startup,
                    mountsTable,
                    InputTracker.CreateDisabledTracker(loggingContext),
                    null,
                    () => FileContentTable.CreateStub(loggingContext),
                    5000,
                    false,
                    controller);

                var pipGraphBuilder = pipGraphFragmentGeneratorConfig.TopSort
                    ? new PipGraphFragmentBuilderTopSort(engineContext, config, mountsTable.MountPathExpander)
                    : new PipGraphFragmentBuilder(engineContext, config, mountsTable.MountPathExpander);

                // Observe mount table is completed during workspace construction
                AddConfigurationMounts(config, mountsTable);

                if (!mountsTable.PopulateModuleMounts(config.ModulePolicies.Values, out var moduleMountsTableMap))
                {
                    Contract.Assume(loggingContext.ErrorWasLogged, "An error should have been logged after MountTable.PopulateModuleMounts()");
                    return false;
                }

                using (frontEndEngineAbstraction is IDisposable ? (IDisposable)frontEndEngineAbstraction : null)
                {
                    if (!controller.PopulateGraph(
                        cache: cache,
                        graph: pipGraphBuilder,
                        engineAbstraction: frontEndEngineAbstraction,
                        evaluationFilter: evaluationFilter,
                        configuration: config,
                        startupConfiguration: mutableCommandlineConfig.Startup))
                    {
                        // Error should have been reported already
                        return false;
                    }

                    if (!SerializeFragmentIfRequested(pipGraphFragmentGeneratorConfig, frontEndContext, pipGraphBuilder))
                    {
                        // Error should have been reported already
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SerializeFragmentIfRequested(
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentGeneratorConfig,
            FrontEndContext context,
            IPipScheduleTraversal pipGraph)
        {
            Contract.Requires(context != null);
            Contract.Requires(pipGraph != null);

            if (!pipGraphFragmentGeneratorConfig.OutputFile.IsValid)
            {
                return true;
            }

            try
            {
                var serializer = new PipGraphFragmentSerializer(context, new PipGraphFragmentContext())
                {
                    AlternateSymbolSeparator = pipGraphFragmentGeneratorConfig.AlternateSymbolSeparator
                };

                serializer.Serialize(
                    pipGraphFragmentGeneratorConfig.OutputFile, 
                    pipGraph,
                    pipGraphFragmentGeneratorConfig.Description,
                    pipGraphFragmentGeneratorConfig.TopSort);

                Logger.Log.GraphFragmentSerializationStats(context.LoggingContext, serializer.FragmentDescription, serializer.Stats.ToString());

                return true;
            }
            catch (Exception e)
            {
                Logger.Log.GraphFragmentExceptionOnSerializingFragment(
                    context.LoggingContext, 
                    pipGraphFragmentGeneratorConfig.OutputFile.ToString(context.PathTable), 
                    e.ToString());

                return false;
            }
        }

        private static void AddConfigurationMounts(IConfiguration config, MountsTable mountsTable)
        {
            // Add configuration mounts
            foreach (var mount in config.Mounts)
            {
                mountsTable.AddResolvedMount(mount, new LocationData(config.Layout.PrimaryConfigFile, 0, 0));
            }
        }

        private static CommandLineConfiguration CompleteCommandLineConfiguration(ICommandLineConfiguration commandLineConfig)
        {
            return new CommandLineConfiguration (commandLineConfig)
            {
                FrontEnd =
                {
                    DebugScript = false,
                    PreserveFullNames = true,
                    PreserveTrivia = false,
                    CancelParsingOnFirstFailure = true,
                    UseSpecPublicFacadeAndAstWhenAvailable = false,
                    ConstructAndSaveBindingFingerprint = false,
                    NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                    UsePackagesFromFileSystem = false,

                    // Garabage collection has a tendency to hang when the machine is really overscheduled.
                    // Pip graph fragment creation typically happens inside a BuildXL pip
                    // so we can rely on BuildXL to manage memory instead of the fragment creation pip.
                    ReleaseWorkspaceBeforeEvaluation = false,
                    UnsafeOptimizedAstConversion = true,
                    AllowUnsafeAmbient = true,
                },
                Engine =
                {
                    Phase = EnginePhases.Schedule
                },
                Schedule =
                {
                    UseFixedApiServerMoniker = true
                },
                Logging =
                {
                    LogsToRetain = 0,
                },
                Cache =
                {
                    CacheSpecs = SpecCachingOption.Disabled
                },
                DisableInBoxSdkSourceResolver = true,
            };
        }

        /// <summary>
        /// Generates pip graph fragment.
        /// </summary>
        public static bool TryGeneratePipGraphFragment(
            LoggingContext loggingContext,
            PathTable pathTable,
            ICommandLineConfiguration commandLineConfig,
            PipGraphFragmentGeneratorConfiguration pipGraphFragmentConfig)
        {
            var fileSystem = new PassThroughFileSystem(pathTable);
            var engineContext = EngineContext.CreateNew(CancellationToken.None, pathTable, fileSystem);
            
            FrontEndContext context = engineContext.ToFrontEndContext(loggingContext, commandLineConfig.FrontEnd);
            Contract.Assert(context.CredentialScanner is NoOpCredentialScanner, "We do not enable credential scanning here as this code runs in a separate process which has separate logging and telemetry. To avoid this issue we are adding the required credential scanner logic to PipGraphFragmentManager where all the graph fragments are merged into a full graph.");

            // Parse filter string.
            var evaluationFilter = EvaluationFilter.Empty;
            if (!string.IsNullOrWhiteSpace(commandLineConfig.Filter))
            {
                if (!TryGetEvaluationFilter(loggingContext, engineContext, commandLineConfig.Filter, out evaluationFilter))
                {
                    // Error should have been been reported already.
                    return false;
                }
            }

            if (!TryBuildPipGraphFragment(
                commandLineConfig,
                pipGraphFragmentConfig,
                context,
                engineContext,
                evaluationFilter))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetEvaluationFilter(
            LoggingContext loggingContext, 
            EngineContext engineContext, 
            string filter, 
            out EvaluationFilter evaluationFilter)
        {
            FilterParser parser = new FilterParser(
                engineContext,
                DummyPathResolver,
                filter);
            RootFilter rootFilter;
            FilterParserError error;
            if (!parser.TryParse(out rootFilter, out error))
            {
                Logger.Log.ErrorParsingFilter(loggingContext, filter, error.Position, error.Message, error.FormatFilterPointingToPosition(filter));
                evaluationFilter = null;
                return false;
            }

            evaluationFilter = rootFilter.GetEvaluationFilter(engineContext.SymbolTable, engineContext.PathTable);
            return true;
        }

        private static bool DummyPathResolver(string s, out AbsolutePath path)
        {
            // The dummy path returned must be valid
            path = new AbsolutePath(1);
            return true;
        }
    }
}
