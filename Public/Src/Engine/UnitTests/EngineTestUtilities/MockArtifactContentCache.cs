// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core.Tasks;
using static BuildXL.Utilities.Core.FormattableStringEx;
using OperationHints = BuildXL.Cache.ContentStore.Interfaces.Sessions.OperationHints;

namespace Test.BuildXL.EngineTestUtilities
{
    /// <summary>
    /// Mock implementation of artifact content cache.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="InMemoryArtifactContentCache"/>, this implementation is backed by files and thus
    /// the file realization mode is implemented faithfully.
    /// </remarks>
    public sealed class MockArtifactContentCache : IArtifactContentCacheForTest
    {
        private readonly object m_lock = new object();
        private readonly string m_rootPath;
        private readonly string m_localRootPath;
        private readonly string m_remoteRootPath;

        private readonly ConcurrentDictionary<ContentHash, CacheEntry> m_content = new ConcurrentDictionary<ContentHash, CacheEntry>();
        private readonly ConcurrentDictionary<string, FileRealizationMode> m_pathRealizationModes = new ConcurrentDictionary<string, FileRealizationMode>();

        private sealed class CacheEntry
        {
            /// <remarks>
            /// We only need to model local vs. remote storage for testing scenarios in which 'bytes transferred' / 'files transferred' metrics
            /// need to be authentic.
            /// </remarks>
            public CacheSites Sites;

            public CacheEntry(CacheSites sites)
            {
                Sites = sites;
            }

        }

        /// <nodoc />
        public MockArtifactContentCache(string rootPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(rootPath));

            m_rootPath = rootPath;
            m_localRootPath = Path.Combine(rootPath, "LOCAL");
            m_remoteRootPath = Path.Combine(rootPath, "REMOTE");

            PrepareDirectory(m_localRootPath);
            PrepareDirectory(m_remoteRootPath);
        }

        private void PrepareDirectory(string directoryPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(directoryPath));

            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            FileUtilities.DeleteDirectoryContents(directoryPath, deleteRootDirectory: false);
        }

        /// <summary>
        /// Gets cache path from content hash.
        /// </summary>
        public string GetCachePathFromContentHash(ContentHash hash) => GetLocalPath(hash);

        private string GetFileNameFromHash(ContentHash hash) => hash.ToString().Replace(':', '_').ToUpperInvariant();

        private string GetLocalPath(ContentHash hash) => Path.Combine(m_localRootPath, GetFileNameFromHash(hash));

        private string GetRemotePath(ContentHash hash) => Path.Combine(m_remoteRootPath, GetFileNameFromHash(hash));

        private void GetPaths(ContentHash hash, out string localPath, out string remotePath)
        {
            localPath = GetLocalPath(hash);
            remotePath = GetRemotePath(hash);
        }

        private long Download(ContentHash hash)
        {
            GetPaths(hash, out string localPath, out string remotePath);
            Contract.Assert(File.Exists(remotePath));

            return Copy(remotePath, localPath);
        }

        private long Upload(ContentHash hash)
        {
            GetPaths(hash, out string localPath, out string remotePath);
            Contract.Assert(File.Exists(localPath));

            return Copy(localPath, remotePath);
        }

        private long Copy(string source, string target)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(source));
            Contract.Requires(!string.IsNullOrWhiteSpace(target));
            Contract.Requires(File.Exists(source));

            var fileInfo = new FileInfo(source);
            fileInfo.CopyTo(target, overwrite: true);

            return fileInfo.Length;
        }

        private void EnsureLocal(ContentHash hash)
        {
            bool entryExists = m_content.TryGetValue(hash, out CacheEntry entry);
            Contract.Assert(entryExists);
            Contract.Assert(entry.Sites != CacheSites.None);

            GetPaths(hash, out string localPath, out string remotePath);

            if ((entry.Sites & CacheSites.Local) == 0)
            {
                Contract.Requires((entry.Sites & CacheSites.Remote) != 0);
                ExceptionUtilities.HandleRecoverableIOException(
                    () => Download(hash),
                    ex => throw new BuildXLException(I($"Unable to ensure locality of content hash '{hash.ToString()}'")));
            }
            else
            {
                Contract.Assert(File.Exists(localPath));
            }

            entry.Sites |= CacheSites.Local;
        }

        private void EnsureRemote(ContentHash hash)
        {
            bool entryExists = m_content.TryGetValue(hash, out CacheEntry entry);
            Contract.Assert(entryExists);
            Contract.Assert(entry.Sites != CacheSites.None);

            GetPaths(hash, out string localPath, out string remotePath);

            if ((entry.Sites & CacheSites.Remote) == 0)
            {
                Contract.Requires((entry.Sites & CacheSites.Local) != 0);
                ExceptionUtilities.HandleRecoverableIOException(
                    () => Upload(hash),
                    ex => throw new BuildXLException(I($"Unable to ensure remote replication of content hash '{hash.ToString()}'")));
            }
            else
            {
                Contract.Assert(File.Exists(remotePath));
            }

            entry.Sites |= CacheSites.Remote;
        }

        private static bool ShouldAttemptHardLink(FileRealizationMode fileRealizationMode) =>
            fileRealizationMode == FileRealizationMode.HardLink
            || fileRealizationMode == FileRealizationMode.HardLinkOrCopy;

        private static bool ShouldAttemptCopy(FileRealizationMode fileRealizationMode) =>
            fileRealizationMode == FileRealizationMode.Copy
            || fileRealizationMode == FileRealizationMode.HardLinkOrCopy;

        private static CreateHardLinkStatus TryCreateHardLink(string source, string target, bool replaceExisting)
        {
            // We swap target and source.
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return FileUtilities.TryCreateHardLinkViaSetInformationFile(target, source, replaceExisting);
            }
            else
            {
                return FileUtilities.TryCreateHardLink(target, source);
            }
        }

        private CreateHardLinkStatus PlaceLinkFromCache(string target, ContentHash contentHash)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(target));

            string cachePath = GetLocalPath(contentHash);
            return TryCreateHardLink(cachePath, target, true);
        }

        private async Task<Possible<Unit, Failure>> PlaceFileInternalAsync(string target, ContentHash contentHash, FileRealizationMode fileRealizationMode)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(target));

            // IArtifactContentCache prescribes that materialization always produces a 'new' file.
            var mayBeDeleted = FileUtilities.TryDeletePathIfExists(target);

            if (!mayBeDeleted.Succeeded)
            {
                return mayBeDeleted.Failure;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));

            string cachePath = GetLocalPath(contentHash);

            Contract.Assert(File.Exists(cachePath));

            bool shouldAttemptHardLink = ShouldAttemptHardLink(fileRealizationMode);

            if (shouldAttemptHardLink)
            {
                CreateHardLinkStatus createHardLinkStatus = PlaceLinkFromCache(target, contentHash);
                if (createHardLinkStatus == CreateHardLinkStatus.Success)
                {
                    return Unit.Void;
                }
                else if (!ShouldAttemptCopy(fileRealizationMode))
                {
                    return new Failure<string>(I($"Unable to place file to '{target}' from cache due to failure in creating hard link: '{createHardLinkStatus}'"));
                }
            }
            
            if (!await CopyFileInternalAsync(cachePath, target))
            {
                return new Failure<string>(I($"Unable to place file '{target}' from cache due to copy failure"));
            }

            return Unit.Void;
        }

        private async Task<Possible<Unit, Failure>> PutFileInternalAsync(string source, ContentHash contentHash, FileRealizationMode fileRealizationMode)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(source));
            Contract.Requires(File.Exists(source));

            string cachePath = GetLocalPath(contentHash);

            bool shouldAttemptHardLink = ShouldAttemptHardLink(fileRealizationMode);

            if (shouldAttemptHardLink)
            {
                CreateHardLinkStatus createHardLinkStatus = !File.Exists(cachePath)
                    ? TryCreateHardLink(source, cachePath, replaceExisting: false) // Content is not in cache.
                    : PlaceLinkFromCache(source, contentHash);    // Content is in the cache.
                if (createHardLinkStatus == CreateHardLinkStatus.Success)
                {
                    return Unit.Void;
                }
                else if (!ShouldAttemptCopy(fileRealizationMode))
                {
                    return new Failure<string>(I($"Unable to put file '{source}' to cache due to failure in creating hard link: '{createHardLinkStatus}'"));
                }
            }

            if (!(await CopyFileInternalAsync(source, cachePath)))
            {
                return new Failure<string>(I($"Unable to put file '{source}' to cache due to copy failure"));
            }

            return Unit.Void;
        }

        private byte[] CalculateBytes(Stream content)
        {
            byte[] contentBytes = ExceptionUtilities.HandleRecoverableIOException(
                                    () =>
                                    {
                                        MemoryStream memoryStream;
                                        Stream streamToRead;
                                        if (!content.CanSeek)
                                        {
                                            memoryStream = new MemoryStream();
                                            streamToRead = memoryStream;
                                        }
                                        else
                                        {
                                            memoryStream = null;
                                            streamToRead = content;
                                        }

                                        using (memoryStream)
                                        {
                                            if (memoryStream != null)
                                            {
                                                content.CopyTo(memoryStream);
                                                memoryStream.Position = 0;
                                            }

                                            Contract.Assert(streamToRead.CanSeek);
                                            Contract.Assume(streamToRead.Length <= int.MaxValue);
                                            var length = (int)streamToRead.Length;
                                            var contentBytesLocal = new byte[length];
                                            int read = 0;
                                            while (read < length)
                                            {
                                                int readThisIteration = streamToRead.Read(contentBytesLocal, read, length - read);
                                                if (readThisIteration == 0)
                                                {
                                                    throw new BuildXLException("Unexpected end of stream");
                                                }

                                                read += readThisIteration;
                                            }

                                            return contentBytesLocal;
                                        }
                                    },
                                    ex => throw new BuildXLException("Undable to calculate bytes from stream"));

            return contentBytes;
        }

        private void DeleteArtifact(ContentHash hash, CacheSites sites)
        {
            GetPaths(hash, out string localPath, out string remotePath);
            if ((sites & CacheSites.Local) != 0 && File.Exists(localPath))
            {
                FileUtilities.DeleteFile(localPath, retryOnFailure: true);
            }

            if ((sites & CacheSites.Remote) != 0 && File.Exists(remotePath))
            {
                FileUtilities.DeleteFile(remotePath, retryOnFailure: true);
            }
        }

        /// <inheritdoc />
        public FileRealizationMode GetRealizationMode(string path)
        {
            return m_pathRealizationModes[path];
        }

        /// <inheritdoc />
        public void ReinitializeRealizationModeTracking()
        {
            m_pathRealizationModes.Clear();
        }

        /// <inheritdoc />
        public void Clear()
        {
            foreach (var kvp in m_content)
            {
                DeleteArtifact(kvp.Key, kvp.Value.Sites);
            }

            m_content.Clear();
            ReinitializeRealizationModeTracking();
            PrepareDirectory(m_localRootPath);
            PrepareDirectory(m_remoteRootPath);
        }

        /// <inheritdoc />
        public void DiscardContentIfPresent(ContentHash content, CacheSites sitesToDiscardFrom)
        {
            lock (m_lock)
            {
                CacheEntry entry;
                if (m_content.TryGetValue(content, out entry))
                {
                    DeleteArtifact(content, sitesToDiscardFrom);
                    CacheSites newSites = entry.Sites & ~sitesToDiscardFrom;

                    if (newSites == CacheSites.None)
                    {
                        m_content.TryRemove(content, out entry);
                    }
                    else
                    {
                        entry.Sites = newSites;
                    }
                }
            }
        }

        /// <inheritdoc />
        public CacheSites FindContainingSites(ContentHash hash)
        {
            lock (m_lock)
            {
                CacheEntry entry;
                bool available = m_content.TryGetValue(hash, out entry);
                return available ? entry.Sites : CacheSites.None;
            }
        }

        /// <inheritdoc />
        public Task<Possible<ContentAvailabilityBatchResult, Failure>> TryLoadAvailableContentAsync(IReadOnlyList<ContentHash> hashes, CancellationToken cancellationToken, OperationHints hints)
        {
            return Task.Run<Possible<ContentAvailabilityBatchResult, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        bool allAvailable = true;
                        var results = new ContentAvailabilityResult[hashes.Count];
                        for (int i = 0; i < hashes.Count; i++)
                        {
                            CacheEntry entry;
                            bool available = m_content.TryGetValue(hashes[i], out entry) && entry.Sites != CacheSites.None;
                            long bytesTransferredRemotely = 0;

                            if (available)
                            {
                                if ((entry.Sites & CacheSites.Local) == 0)
                                {
                                    ExceptionUtilities.HandleRecoverableIOException(
                                        () =>
                                        {
                                            // Copy to local side.
                                            bytesTransferredRemotely = Download(hashes[i]);
                                        },
                                        ex => throw new BuildXLException(I($"Failed to load content '{hashes[i]}' from remote site"), ex));
                                }

                                entry.Sites |= CacheSites.Local;
                            }

                            results[i] = new ContentAvailabilityResult(
                                hashes[i],
                                isAvailable: available,
                                bytesTransferred: bytesTransferredRemotely,
                                sourceCache: nameof(MockArtifactContentCache));
                            allAvailable &= available;
                        }

                        return new ContentAvailabilityBatchResult(
                            ReadOnlyArray<ContentAvailabilityResult>.FromWithoutCopy(results),
                            allContentAvailable: allAvailable);
                    }
                });
        }

        /// <inheritdoc />
        public Task<Possible<StreamWithLength, Failure>> TryOpenContentStreamAsync(ContentHash contentHash)
        {
            return Task.Run<Possible<StreamWithLength, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            if ((entry.Sites & CacheSites.Local) == 0)
                            {
                                return new Failure<string>("Content is available in 'remote' cache but is not local. Load it locally first with TryLoadAvailableContentAsync.");
                            }

                            string localPath = GetLocalPath(contentHash);
                            Contract.Assert(File.Exists(localPath));

                            return (new FileStream(localPath, FileMode.Open, FileAccess.Read)).WithLength();
                        }
                        else
                        {
                            return new Failure<string>("Content not found (locally or remotely). Store it first with TryStoreAsync.");
                        }
                    }
                });
        }

        /// <inheritdoc />
        public Task<Possible<Unit, Failure>> TryMaterializeAsync(
            FileRealizationMode fileRealizationMode,
            ExpandedAbsolutePath path,
            ContentHash contentHash,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () =>
                {
                    lock (m_lock)
                    {
                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            if ((entry.Sites & CacheSites.Local) == 0)
                            {
                                return new Failure<string>("Content is available in 'remote' cache but is not local. Load it locally first with TryLoadAvailableContentAsync.");
                            }

                            string expandedPath = path.ExpandedPath;

                            var result = ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                                async () => await PlaceFileInternalAsync(expandedPath, contentHash, fileRealizationMode),
                                ex => { throw new BuildXLException("Failed to materialize content (content found, but couldn't write it)", ex); });

                            m_pathRealizationModes[expandedPath] = fileRealizationMode;
                            return result.Result;
                        }
                        else
                        {
                            return new Failure<string>("Content not found (locally or remotely). Store it first with TryStoreAsync.");
                        }
                    }
                });
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            ContentHash contentHash,
            StoreArtifactOptions options = default)
        {
            Possible<ContentHash, Failure> maybeStored = await TryStoreInternalAsync(
                path,
                fileRealizationModes,
                knownContentHash: contentHash);
            return maybeStored.Then(hash => Unit.Void);
        }

        /// <inheritdoc />
        public Task<Possible<ContentHash, Failure>> TryStoreAsync(
            FileRealizationMode fileRealizationModes,
            ExpandedAbsolutePath path,
            StoreArtifactOptions options = default)
        {
            return TryStoreInternalAsync(
                path,
                fileRealizationModes,
                knownContentHash: null);
        }

        private static Possible<ContentHash> TryHashFile(string path)
        {
            try
            {
                return ContentHashingUtilities.HashFileAsync(path).Result;
            }
            catch (System.Exception ex)
            {
                return new Failure<string>(I($"Failed to hash '{path}': {ex.GetLogEventMessage()}"));
            }
        }

        private Task<Possible<ContentHash, Failure>> TryStoreInternalAsync(
            ExpandedAbsolutePath path,
            FileRealizationMode fileRealizationMode,
            ContentHash? knownContentHash)
        {
            return Task.Run<Possible<ContentHash, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        string expandedPath = path.ExpandedPath;
                        ContentHash contentHash;

                        if (knownContentHash.HasValue)
                        {
                            contentHash = knownContentHash.Value;

                            var mayBeHash = TryHashFile(expandedPath);
                            if (!mayBeHash.Succeeded)
                            {
                                return mayBeHash.Failure;
                            }

                            if (contentHash != mayBeHash.Result)
                            {
                                return new Failure<string>(I($"Stored content had an unexpected hash. (expected: {contentHash}; actual: {mayBeHash.Result})"));
                            }
                        }
                        else
                        {
                            var maybeHash = TryHashFile(expandedPath);
                            if (!maybeHash.Succeeded)
                            {
                                return maybeHash.Failure;
                            }

                            contentHash = maybeHash.Result;
                        }

                        string localPath = GetLocalPath(contentHash);

                        CacheEntry entry;
                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            EnsureLocal(contentHash);
                            EnsureRemote(contentHash);

                            var putFile = PutFileInternalAsync(expandedPath, contentHash, fileRealizationMode).Result;
                            if (!putFile.Succeeded)
                            {
                                return putFile.Failure;
                            }

                            return contentHash;
                        }
                        else
                        {
                            var putFile = PutFileInternalAsync(expandedPath, contentHash, fileRealizationMode).Result;
                            if (!putFile.Succeeded)
                            {
                                return putFile.Failure;
                            }

                            m_content.Add(contentHash, new CacheEntry(CacheSites.Local));
                            EnsureRemote(contentHash);
                            m_pathRealizationModes[expandedPath] = fileRealizationMode;

                            return contentHash;
                        }
                    }
                });
        }

        /// <inheritdoc />
        public async Task<Possible<Unit, Failure>> TryStoreAsync(Stream content, ContentHash contentHash, StoreArtifactOptions options = default)
        {
            Possible<ContentHash, Failure> maybeStored = await TryStoreInternalAsync(content, knownContentHash: null);
            return maybeStored.Then(hash => Unit.Void);
        }

        private Task<Possible<ContentHash, Failure>> TryStoreInternalAsync(Stream content, ContentHash? knownContentHash)
        {
            return Task.Run<Possible<ContentHash, Failure>>(
                () =>
                {
                    lock (m_lock)
                    {
                        byte[] contentBytes = CalculateBytes(content);
                        ContentHash contentHash = knownContentHash ?? ContentHashingUtilities.HashBytes(contentBytes);
                        CacheEntry entry;

                        if (m_content.TryGetValue(contentHash, out entry))
                        {
                            EnsureLocal(contentHash);
                            EnsureRemote(contentHash);
                        }
                        else
                        {
                            ExceptionUtilities.HandleRecoverableIOException(
                                () => File.WriteAllBytes(GetLocalPath(contentHash), contentBytes),
                                ex => throw new BuildXLException("Unable to store to cache from stream"));
                            m_content.Add(contentHash, new CacheEntry(CacheSites.Local));
                            EnsureRemote(contentHash);
                        }

                        return contentHash;
                    }
                });
        }

        private static Task<bool> CopyFileInternalAsync(string source, string destination)
        {
            if (FileUtilitiesExtensions.IsCopyOnWriteSupportedByEnlistmentVolume)
            {
                return Task.Run(async () =>
                {
                    var possiblyCreateCopyOnWrite = FileUtilitiesExtensions.TryCreateCopyOnWrite(source, destination, followSymlink: false);

                    if (!possiblyCreateCopyOnWrite.Succeeded)
                    {
                        return await FileUtilities.CopyFileAsync(source, destination);
                    }

                    return possiblyCreateCopyOnWrite.Succeeded;
                });
            }

            return FileUtilities.CopyFileAsync(source, destination);
        }
    }
}
