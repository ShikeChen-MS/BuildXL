// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Interface for generic warning handling.
    /// </summary>
    public interface IWarningHandling
    {
        /// <summary>
        /// Treat warnings as errors
        /// </summary>
        bool TreatWarningsAsErrors { get; }

        /// <summary>
        /// Explit list of warnings that should be errors
        /// </summary>
        [NotNull]
        IReadOnlyList<int> WarningsAsErrors { get; }

        /// <summary>
        /// Warnings that explicitly should not be treated as errors.
        /// </summary>
        [NotNull]
        IReadOnlyList<int> WarningsNotAsErrors { get; }

        /// <summary>
        /// Warnings to suppress
        /// </summary>
        [NotNull]
        IReadOnlyList<int> NoWarnings { get; }
    }
}
