// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities.Core;
using Counter = BuildXL.FrontEnd.Workspaces.Counter;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Captures statistic information about different stages of the DScript frontend pipeline.
    /// </summary>
    public sealed class FrontEndStatistics : WorkspaceStatistics, IFrontEndStatistics
    {
        private long m_analysisDurationInTicks;

        /// <nodoc />
        public FrontEndStatistics(EventHandler<WorkspaceProgressEventArgs> workspaceProgressHandler = null)
        {
            WorkspaceProgress = workspaceProgressHandler;
        }

        /// <inheritdoc />
        public Counter SpecAstConversion { get; } = new Counter();

        /// <inheritdoc />
        public Counter SpecAstDeserialization { get; } = new Counter();

        /// <inheritdoc />
        public Counter SpecAstSerialization { get; } = new Counter();

        /// <inheritdoc />
        public Counter PublicFacadeComputation { get; } = new Counter();

        /// <inheritdoc />
        public CounterWithRootCause CounterWithRootCause { get; } = new CounterWithRootCause();

        /// <inheritdoc />
        public Counter ConfigurationProcessing { get; } = new Counter();

        /// <inheritdoc />
        public Counter PreludeProcessing { get; } = new Counter();

        /// <inheritdoc />
        void IFrontEndStatistics.AnalysisCompleted(AbsolutePath path, TimeSpan duration)
        {
            Interlocked.Add(ref m_analysisDurationInTicks, duration.Ticks);
        }

        /// <inheritdoc />
        TimeSpan IFrontEndStatistics.GetOverallAnalysisDuration()
        {
            return TimeSpan.FromTicks(Interlocked.Read(ref m_analysisDurationInTicks));
        }

        /// <inheritdoc />
        public EventHandler<WorkspaceProgressEventArgs> WorkspaceProgress { get; }

        /// <inheritdoc />
        public INugetStatistics NugetStatistics { get; } = new NugetStatistics();

        /// <inheritdoc />
        public ILoadConfigStatistics LoadConfigStatistics { get; } = new LoadConfigStatistics();
    }
}
