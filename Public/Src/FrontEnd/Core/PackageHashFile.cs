// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Tracing;
using TypeScript.Net.Extensions;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Represents a content of a package's hash file.
    /// </summary>
    /// <remarks>
    /// This file is used for speed up-to-date check that a nuget package already layed out on disk can be reused without touching the cache or nuget.
    /// </remarks>
    internal readonly struct PackageHashFile
    {
        // The hash file version is separated to cover the following cases:
        //  * the file format itself, and
        //  * the format of the generated specs.
        // If the file format changes, we have to ignore the files on disk,
        // buf if the file format is the same and only generated specs format has changed,
        // then we can reuse files from disk and force specs regeneration.

        // The file format change will force specs regeneration.
        // Change the version if the nuget spec generation has changed in a backward incompatible way.
        private const string HashFileFormatVersion = "10";

        /// <summary>
        /// The minimal number of lines for the hash file.
        /// </summary>
        private const int MinNumberOfLines = 3;

        /// <summary>
        /// Fingerprint hash of a nuget package.
        /// </summary>
        public string FingerprintHash { get; }

        /// <summary>
        /// Full fingerprint text of a nuget package.
        /// </summary>
        public string FingerprintText { get; }

        /// <summary>
        /// Package content. List of relative paths.
        /// </summary>
        public IReadOnlyList<string> Content { get; }

        /// <nodoc/>
        public PackageHashFile(string fingerprintHash, string fingerprintText, IReadOnlyList<string> content)
        {
            Contract.Requires(!string.IsNullOrEmpty(fingerprintHash));
            Contract.Requires(!string.IsNullOrEmpty(fingerprintText));
            // Empty content means that something went wrong. The caller should make sure this never happen.
            Contract.Requires(!content.IsNullOrEmpty(), "Hash file with an empty content will lead to a break on the following invocation.");

            FingerprintHash = fingerprintHash;
            FingerprintText = fingerprintText;
            Content = new List<string>(content.OrderBy(id => id));
        }

        /// <summary>
        /// Tries to read a package's hash file from disk.
        /// </summary>
        /// <remarks>
        /// The <see cref="Failure"/> returned from this method is recoverable.
        /// </remarks>
        public static Possible<PackageHashFile> TryReadFrom(string path)
        {
            string[] content;
            try
            {
                content = ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        if (!File.Exists(path))
                        {
                            return null;
                        }

                        return File.ReadAllLines(path);
                    },
                    e => throw new BuildXLException(FormattableStringEx.I($"Failed to parse package hash file."), e));
            }
            catch (BuildXLException e)
            {
                return new PackageHashFileFailure(e.LogEventMessage);
            }

            if (content == null)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file is missing."));
            }

            if (content.Length < MinNumberOfLines)
            {
                // The version field potentially can be used for invalidating generated packages as well.
                // The new file format is:
                // Version
                // Specs format version
                // SHA
                // Fingerprint
                // List of files
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file has an invalid content. Expected at least {MinNumberOfLines} lines but got {content.Length} lines."));
            }

            var version = content[0];
            if (version != HashFileFormatVersion)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file has different version. Expected version: {HashFileFormatVersion}, actual version: {version}."));
            }

            var fingerprintHash = content[1];
            var fingerprintText = content[2];

            var files = content.Skip(MinNumberOfLines).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            if (files.Length == 0)
            {
                return new PackageHashFileFailure(FormattableStringEx.I($"Package hash file does not have package content files."));
            }

            return new PackageHashFile(fingerprintHash, fingerprintText, files);
        }

        /// <summary>
        /// Tries to save package's hash file to disk.
        /// </summary>
        internal static Possible<Unit> TrySaveTo(string path, PackageHashFile hashFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HashFileFormatVersion)
                .AppendLine(hashFile.FingerprintHash)
                .AppendLine(hashFile.FingerprintText);

            foreach (var file in hashFile.Content)
            {
                sb.AppendLine(file);
            }

            try
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        // CodeQL [SM04881] The path is prepared by the caller and is controlled elsewhere in FrontEnd
                        File.WriteAllText(path, sb.ToString());
                    },
                    e => throw new BuildXLException("Failed to save a package hash file.", e));

                return Unit.Void;
            }
            catch (BuildXLException e)
            {
                return new PackageHashFileFailure(e.LogEventMessage);
            }
        }

        /// <summary>
        /// Returns the fingeprint hash and the text for logging purposes.
        /// </summary>
        public string FingerprintWithHash()
        {
            return string.Join(Environment.NewLine, FingerprintHash, FingerprintText);
        }
    }
}
