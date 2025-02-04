﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores;

/// <nodoc />
public static class AzureBlobStorageCacheFactory
{
    /// <summary>
    /// Configuration for <see cref="AzureBlobStorageCacheFactory"/>
    /// </summary>
    /// <param name="ShardingScheme">Sharding scheme to use</param>
    /// <param name="Universe">Cache universe</param>
    /// <param name="Namespace">Cache namespace</param>
    /// <param name="RetentionPolicyInDays">
    /// Set to null to disable engine-side GC if you are using the BlobLifetimeManager to manage the size of the cache.
    /// If not null, if a content hash list is older than this retention period, the engine will manually pin all of its contents to ensure that content still exists.
    /// </param>
    /// <param name="IsReadOnly">Whether the cache is read-only</param>
    public record Configuration(
        ShardingScheme ShardingScheme,
        string Universe,
        string Namespace,
        int? RetentionPolicyInDays,
        bool IsReadOnly)
    {
        /// <summary>
        /// The default universe.
        /// </summary>
        public static readonly string DefaultUniverse = "default";

        /// <summary>
        /// The default namespace.
        /// </summary>
        public static readonly string DefaultNamespace = "default";

        private bool IsServerlessGC => RetentionPolicyInDays != null;

        /// <summary>
        /// Determines behavior for checking content existence for determining whether to replace entry in AddOrGetContentHashList
        /// </summary>
        public ContentHashListReplacementCheckBehavior ContentHashListReplacementCheckBehavior { get; set; } = ContentHashListReplacementCheckBehavior.AllowPinElision;

        /// <summary>
        /// In the case the cache configuration comes from 1ESHP, the 1ES build cache resource configuration that acts as the backing blob
        /// </summary>
        public BuildCacheConfiguration? BuildCacheConfiguration { get; init; }

        /// <summary>
        /// Maximum amount of time we're willing to wait for any operation against storage.
        /// </summary>
        public TimeSpan StorageInteractionTimeout { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Amount of time that content is guaranteed to exist after a fingerprint that points to that piece of
        /// content has been obtained from GetContentHashList.
        /// </summary>
        /// <remarks>
        /// Default is 12 hours. If the specified duration is hit, pins will stop eliding for that content (which is not incorrect, just slower). The default value
        /// takes into consideration that most builds will take under 12 hours.
        /// </remarks>
        public TimeSpan MetadataPinElisionDuration { get; init; } = TimeSpan.FromHours(12);

        /// <summary>
        /// Retry policy for Azure Storage client.
        /// </summary>
        public ShardedBlobCacheTopology.BlobRetryPolicy BlobRetryPolicy { get; init; } = new();
    }

    /// <nodoc />
    public record struct CreateResult
    {
        /// <nodoc />
        public Configuration Configuration { get; }

        /// <nodoc />
        public IFullCache Cache { get; }

        /// <nodoc />
        public IBlobCacheTopology Topology { get; }

        /// <nodoc />
        internal IContentStore ContentStore { get; }

        /// <nodoc />
        internal IMemoizationStore MemoizationStore { get; }

        /// <nodoc />
        internal RemoteNotificationDispatch Announcer { get; }

        /// <nodoc />
        public IBlobCacheContainerSecretsProvider SecretsProvider { get; }

        /// <nodoc />
        internal CreateResult(
            Configuration configuration,
            IFullCache cache,
            IBlobCacheTopology topology,
            IContentStore contentStore,
            IMemoizationStore memoizationStore,
            RemoteNotificationDispatch announcer,
            IBlobCacheContainerSecretsProvider secretsProvider)
        {
            Configuration = configuration;
            Cache = cache;
            Topology = topology;
            ContentStore = contentStore;
            MemoizationStore = memoizationStore;
            Announcer = announcer;
            SecretsProvider = secretsProvider;
        }
    }

    /// <nodoc />
    public static CreateResult Create(OperationContext context, Configuration configuration, IBlobCacheContainerSecretsProvider secretsProvider)
    {
        context.TracingContext.Debug($"Creating cache with BuildXL version {Branding.Version}", nameof(AzureBlobStorageCacheFactory));

        if (string.IsNullOrEmpty(configuration.Universe))
        {
            configuration = configuration with { Universe = Configuration.DefaultUniverse };
        }

        if (string.IsNullOrEmpty(configuration.Namespace))
        {
            configuration = configuration with { Namespace = Configuration.DefaultNamespace };
        }

        // Universe/namespace validation happens only when using the legacy naming scheme
        if (configuration.BuildCacheConfiguration == null)
        {
            LegacyBlobCacheContainerName.CheckValidUniverseAndNamespace(configuration.Universe, configuration.Namespace);
        }

        IBlobCacheTopology topology = CreateTopology(configuration, secretsProvider);

        Contract.Assert((configuration.RetentionPolicyInDays ?? 1) > 0, $"{nameof(configuration.RetentionPolicyInDays)} must be null or greater than 0");

        TimeSpan? retentionPolicyTimeSpan = configuration.RetentionPolicyInDays is null
            ? null
            : TimeSpan.FromDays(configuration.RetentionPolicyInDays.Value);

        var announcer = new RemoteNotificationDispatch();
        AzureBlobStorageContentStore contentStore = CreateContentStore(configuration, topology, announcer);
        IMemoizationStore memoizationStore = CreateMemoizationStore(configuration, topology, retentionPolicyTimeSpan);

        var cache = CreateCache(configuration, contentStore, memoizationStore);

        return new CreateResult(configuration, cache, topology, contentStore, memoizationStore, announcer, secretsProvider);
    }

    private static IFullCache CreateCache(Configuration configuration, IContentStore contentStore, IMemoizationStore memoizationStore)
    {
        return new OneLevelCache(
                        contentStoreFunc: () => contentStore,
                        memoizationStoreFunc: () => memoizationStore,
                        configuration: new OneLevelCacheBaseConfiguration(
                            Id: Guid.NewGuid(),
                            AutomaticallyOverwriteContentHashLists: true,
                            MetadataPinElisionDuration: configuration.MetadataPinElisionDuration
                        ));
    }

    private static IBlobCacheTopology CreateTopology(Configuration configuration, IBlobCacheContainerSecretsProvider secretsProvider)
    {
        return new ShardedBlobCacheTopology(
            new ShardedBlobCacheTopology.Configuration(
                BuildCacheConfiguration: configuration.BuildCacheConfiguration,
                ShardingScheme: configuration.ShardingScheme,
                SecretsProvider: secretsProvider,
                Universe: configuration.Universe,
                Namespace: configuration.Namespace,
                BlobRetryPolicy: configuration.BlobRetryPolicy));
    }

    private static AzureBlobStorageContentStore CreateContentStore(Configuration configuration, IBlobCacheTopology topology, IRemoteContentAnnouncer? announcer)
    {
        return new AzureBlobStorageContentStore(
            new AzureBlobStorageContentStoreConfiguration()
            {
                Topology = topology,
                StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                Announcer = announcer,
                IsReadOnly = configuration.IsReadOnly,
            });
    }

    private static DatabaseMemoizationStore CreateMemoizationStore(
        Configuration configuration,
        IBlobCacheTopology topology,
        TimeSpan? retentionPolicyTimeSpan)
    {
        var blobMetadataStore = new AzureBlobStorageMetadataStore(
            new BlobMetadataStoreConfiguration
            {
                Topology = topology,
                BlobFolderStorageConfiguration = new BlobFolderStorageConfiguration
                {
                    StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                    RetryPolicy = BlobFolderStorageConfiguration.DefaultRetryPolicy,
                },
                IsReadOnly = configuration.IsReadOnly
            });

        // The memoization database will make sure the associated content for a retrieved content
        // hash list is preventively pinned if it runs the risk of being evicted based on the configured retention policy
        // This means that the content store can elide pins for content that is mentioned in get content hash list operations
        var blobMemoizationDatabase = new MetadataStoreMemoizationDatabase(
            blobMetadataStore,
            new MetadataStoreMemoizationDatabaseConfiguration()
            {
                RetentionPolicy = retentionPolicyTimeSpan,
                DisablePreventivePinning = retentionPolicyTimeSpan is null
            });

        bool usesServerGc = retentionPolicyTimeSpan is null;

        return new DatabaseMemoizationStore(blobMemoizationDatabase)
        {
            OptimizeWrites = true,
            ContentHashListReplacementCheckBehavior = configuration.ContentHashListReplacementCheckBehavior
        };
    }
}
