// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Ipc.ExternalApi
{
    /// <summary>
    /// Helper methods for (de)serializing <see cref="BuildXL.Utilities.Core.DirectoryArtifact"/>.
    /// </summary>
    public static class DirectoryId
    {
        /// <summary>
        /// Used for separating rendered values in <see cref="ToString(DirectoryArtifact)"/>
        /// </summary>
        internal const char Separator = ':';

        /// <summary>
        /// Serializes a directory artifact into a string identifier.
        /// </summary>
        public static string ToString(DirectoryArtifact directory)
        {
            return I($"{directory.Path.RawValue}{Separator}{(directory.IsSharedOpaque ? 1 : 0)}{Separator}{directory.PartialSealId}");
        }

        /// <summary>
        /// Parses a DirectoryId from a string value.  Delegates to <see cref="TryParse"/>;
        /// if unsuccessful, throws <see cref="ArgumentException"/>.
        /// </summary>
        public static DirectoryArtifact Parse(string value)
        {
            DirectoryArtifact result;
            if (!TryParse(value, out result))
            {
                throw new ArgumentException("invalid directory id: " + value);
            }

            return result;
        }

        /// <summary>
        /// Attempts to parse a DirectoryArtifact from a string value.
        /// Expected format is {Path.RawValue}:{IsSharedOpaque(true=1; 0=false):{PartialSealId}}.
        /// </summary>
        public static bool TryParse(string value, out DirectoryArtifact directory)
        {
            directory = DirectoryArtifact.Invalid;

            string[] splits = value.Split(Separator);
            if (splits.Length != 3)
            {
                return false;
            }

            int pathId;
            if (!int.TryParse(splits[0], out pathId))
            {
                return false;
            }

            int isSharedOpaque;
            if (!int.TryParse(splits[1], out isSharedOpaque) || isSharedOpaque < 0 || isSharedOpaque > 1)
            {
                return false;
            }

            uint partialSealId;
            if (!uint.TryParse(splits[2], out partialSealId))
            {
                return false;
            }

            directory = new DirectoryArtifact(new AbsolutePath(pathId), partialSealId, isSharedOpaque == 1);
            return true;
        }
    }
}
