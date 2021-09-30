// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Contracts;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using JetBrains.Annotations;
using Pure = System.Diagnostics.Contracts.PureAttribute;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Item to be added to drop.
    /// </summary>
    public interface IDropItem
    {
        /// <summary>
        /// Full path of the file on disk to be added to drop.  The file need not be
        /// physically present on disk before <see cref="EnsureMaterialized"/> is called.
        /// </summary>
        [NotNull]
        [Pure]
        string FullFilePath { get; }

        /// <summary>
        /// Relative path under which to associate the file with a drop.
        /// </summary>
        [NotNull]
        [Pure]
        string RelativeDropPath { get; }

        /// <summary>
        /// (Optional) Pre-computed blob identifier.
        /// </summary>
        [CanBeNull]
        [Pure]
        BlobIdentifier BlobIdentifier { get; }

        /// <summary>
        /// (Optional) Pre-computed file length.
        /// </summary>
        [Pure]
        long FileLength { get; }

        /// <summary>
        /// Fully qualified name (endpoint + drop name) of a drop
        /// </summary>
        [NotNull]
        [Pure]
        string FullyQualifiedDropName { get; }

        /// <summary>
        /// Full information about the file which is to be added to a drop.  After the completion
        /// of the returned task, the file must exist on disk.  The returned file info must also
        /// match the full file path returned by the <see cref="FullFilePath"/> property.
        /// </summary>
        [NotNull]
        Task<FileInfo> EnsureMaterialized();
    }
}

