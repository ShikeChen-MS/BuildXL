// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Engine;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Factory functions for common uses of <see cref="Scheduler" /> in tests.
    /// </summary>
    public static class TestSchedulerFactory
    {
        /// <summary>
        /// Creates an empty, mutable pip graph. Add pips to it, then create a scheduler.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static PipGraph.Builder CreateEmptyPipGraph(
            EngineContext context,
            IConfiguration configuration,
            SemanticPathExpander semanticPathExpander)
        {
            var pipTable = new PipTable(context.PathTable, context.SymbolTable, initialBufferSize: 16, maxDegreeOfParallelism: 0, debug: true);

            return new PipGraph.Builder(
                pipTable,
                context,
                global::BuildXL.Pips.Tracing.Logger.Log,
                BuildXLTestBase.CreateLoggingContextForTest(),
                configuration,
                semanticPathExpander);
        }

        /// <summary>
        /// Creates a scheduler that runs non-incrementally (no cache).
        /// Artifact content is managed with an <see cref="InMemoryArtifactContentCache"/>, so the total artifact footprint must be small.
        /// The provided pip graph is marked immutable if it isn't already.
        /// Both the scheduler and <see cref="EngineCache"/> must be disposed by the caller.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Caller owns the returned disposables")]
        public static Tuple<Scheduler, EngineCache> Create(
            PipExecutionContext context,
            LoggingContext loggingContext,
            IConfiguration configuration,
            PipGraph.Builder graphBuilder,
            IPipQueue queue)
        {
            Contract.Requires(graphBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(queue != null);
            Contract.Requires(configuration != null);

            var cacheLayer = new EngineCache(
                new InMemoryArtifactContentCache(),
                new EmptyTwoPhaseFingerprintStore());

            Scheduler scheduler = CreateInternal(
                context,
                loggingContext,
                graphBuilder.Build(),
                queue,
                cache: cacheLayer,
                configuration: configuration);

            return Tuple.Create(scheduler, cacheLayer);
        }

        /// <summary>
        /// Creates a scheduler that runs with a fully capable cache for storing artifact content and cache descriptors.
        /// Pips may complete from cache.
        /// Both the scheduler and <see cref="EngineCache"/> must be disposed by the caller.
        /// The provided pip graph is marked immutable if it isn't already.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Caller owns the returned disposables")]
        public static Tuple<Scheduler, EngineCache> CreateWithCaching(
            PipExecutionContext context,
            LoggingContext loggingContext,
            IConfiguration configuration,
            PipGraph.Builder graphBuilder,
            IPipQueue queue)
        {
            Contract.Requires(graphBuilder != null);
            Contract.Requires(context != null);
            Contract.Requires(queue != null);

            var cacheLayer = new EngineCache(
                new InMemoryArtifactContentCache(),
                new InMemoryTwoPhaseFingerprintStore());

            Scheduler scheduler = CreateInternal(
                context,
                loggingContext,
                graphBuilder.Build(),
                queue,
                cacheLayer,
                configuration);

            return Tuple.Create(scheduler, cacheLayer);
        }

        private static Scheduler CreateInternal(
            PipExecutionContext context,
            LoggingContext loggingContext,
            PipGraph pipGraph,
            IPipQueue queue,
            EngineCache cache,
            IConfiguration configuration)
        {
            Contract.Requires(context != null);
            Contract.Requires(queue != null);
            Contract.Requires(cache != null);
            Contract.Requires(configuration != null);

            var fileContentTable = FileContentTable.CreateNew(loggingContext);

            var fileAccessAllowList = new FileAccessAllowlist(context);

            var testHooks = new SchedulerTestHooks();

            return new Scheduler(
                pipGraph,
                queue,
                context,
                fileContentTable,
                cache: cache,
                loggingContext: loggingContext,
                configuration: configuration,
                fileAccessAllowlist: fileAccessAllowList,
                testHooks: testHooks,
                buildEngineFingerprint: null,
                tempCleaner: new TestMoveDeleteCleaner(Path.Combine(Environment.GetEnvironmentVariable("TEMP"), "MoveDeletionTemp")));
        }
    }
}
