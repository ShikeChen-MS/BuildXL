// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class NugetPackage : INugetPackage
    {
        /// <nodoc />
        public NugetPackage()
        {
            OsSkip = new List<string>();
            DependentPackageIdsToSkip = new List<string>();
            DependentPackageIdsToIgnore = new List<string>();
            FilesToExclude = new List<RelativePath>();
        }

        /// <nodoc />
        public NugetPackage(INugetPackage template, PathRemapper pathRemapper)
        {
            Id = template.Id;
            Version = template.Version;
            Alias = template.Alias;
            Tfm = template.Tfm;
            OsSkip = template.OsSkip ?? new List<string>();
            DependentPackageIdsToSkip = template.DependentPackageIdsToSkip ?? new List<string>();
            DependentPackageIdsToIgnore = template.DependentPackageIdsToIgnore ?? new List<string>();
            ForceFullFrameworkQualifiersOnly = template.ForceFullFrameworkQualifiersOnly;
            FilesToExclude = template.FilesToExclude?.Select(relativePath => pathRemapper.Remap(relativePath)).ToList() ?? new List<RelativePath>();
        }

        /// <inheritdoc />
        public string Id { get; set; }

        /// <inheritdoc />
        public string Version { get; set; }

        /// <inheritdoc />
        public string Alias { get; set; }

        /// <inheritdoc />
        public string Tfm { get; set; }

        /// <inheritdoc />
        public List<string> OsSkip { get; private set; }

        /// <inheritdoc />
        public List<string> DependentPackageIdsToSkip { get; set; }

        /// <inheritdoc />
        public List<string> DependentPackageIdsToIgnore { get; set; }

        /// <inheritdoc />
        public bool ForceFullFrameworkQualifiersOnly { get; private set; } = false;

        /// <inheritdoc />
        public List<RelativePath> FilesToExclude { get; private set; }
    }
}
