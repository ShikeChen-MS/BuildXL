// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities.Core;
using Counter = BuildXL.FrontEnd.Workspaces.Counter;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Interface for capturing statistics for different phases of the frontend.
    /// </summary>
    public interface IFrontEndStatistics : IWorkspaceStatistics
    {
        /// <nodoc/>
        void AnalysisCompleted(AbsolutePath path, TimeSpan duration);

        /// <nodoc/>
        TimeSpan GetOverallAnalysisDuration();

        /// <nodoc/>
        Counter SpecAstConversion { get; }

        /// <nodoc/>
        Counter SpecAstDeserialization { get; }

        /// <nodoc/>
        Counter SpecAstSerialization { get; }

        /// <nodoc/>
        Counter PublicFacadeComputation { get; }

        /// <summary>
        /// Counter with the number of reloads of source texts.
        /// </summary>
        CounterWithRootCause CounterWithRootCause { get; }

        /// <summary>
        /// Delegate that will be invoked for workspace computation progress reporting.
        /// </summary>
        [AllowNull]
        EventHandler<WorkspaceProgressEventArgs> WorkspaceProgress { get; }

        /// <summary>
        /// Counter for all processed configuration files.
        /// </summary>
        Counter ConfigurationProcessing { get; }

        /// <summary>
        /// Counter to measure built-in prelude processing times.
        /// </summary>
        Counter PreludeProcessing { get; }

        /// <summary>
        /// Statistics for all nuget-related operations.
        /// </summary>
        INugetStatistics NugetStatistics { get; }

        /// <summary>
        /// Statistics for configuration processing.
        /// </summary>
        ILoadConfigStatistics LoadConfigStatistics { get; }
    }
}
