// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.ConfigurationHelper;

namespace BuildXL.Cache.ContentStore.Distributed.Services
{
    /// <nodoc />
    public record DistributedContentStoreServicesArguments(
        DistributedContentSettings DistributedContentSettings,
        GrpcConnectionMap ConnectionMap,
        LocalLocationStoreConfiguration ContentLocationStoreConfiguration,
        DistributedCacheServiceHostOverrides Overrides,
        IDistributedServicesSecrets Secrets,
        Interfaces.FileSystem.AbsolutePath PrimaryCacheRoot,
        IAbsFileSystem FileSystem,
        DistributedContentCopier DistributedContentCopier,
        IContentStore PreferredContentStore)
    {
        public IClock Clock => Overrides.Clock;
    }

    /// <nodoc />
    public interface IDistributedServicesSecrets
    {
        /// <nodoc />
        Secret GetRequiredSecret(string secretName);

        /// <nodoc />
        SecretBasedAzureStorageCredentials[] GetStorageCredentials(IEnumerable<string> storageSecretNames);
    }

    /// <summary>
    /// Services used by for distributed content store
    /// </summary>
    public class DistributedContentStoreServices : ServicesCreatorBase
    {
        /// <nodoc />
        public DistributedContentStoreServicesArguments Arguments { get; }

        /// <nodoc />
        private DistributedContentSettings DistributedContentSettings => Arguments.DistributedContentSettings;

        /// <nodoc />
        private LocalLocationStoreConfiguration ContentLocationStoreConfiguration => Arguments.ContentLocationStoreConfiguration;

        /// <nodoc />
        public IServiceDefinition<GlobalCacheServiceConfiguration> GlobalCacheServiceConfiguration { get; }

        /// <nodoc />
        public OptionalServiceDefinition<GlobalCacheService> GlobalCacheService { get; }

        /// <nodoc />
        public IServiceDefinition<CheckpointManager> GlobalCacheCheckpointManager { get; }

        /// <nodoc />
        public IServiceDefinition<CentralStreamStorage> GlobalCacheStreamStorage { get; }

        /// <nodoc />
        public IServiceDefinition<RocksDbContentMetadataStore> RocksDbContentMetadataStore { get; }

        /// <nodoc />
        public OptionalServiceDefinition<IRoleObserver> RoleObserver { get; }

        /// <nodoc />
        public IServiceDefinition<ContentLocationStoreFactory> ContentLocationStoreFactory { get; }

        /// <nodoc />
        public IServiceDefinition<ContentLocationStoreServices> ContentLocationStoreServices { get; }

        internal IServiceDefinition<ICheckpointRegistry> CacheServiceCheckpointRegistry { get; }

        internal DistributedContentStoreServices(DistributedContentStoreServicesArguments arguments)
        {
            Arguments = arguments;

            bool isGlobalCacheServiceEnabled = DistributedContentSettings.IsMasterEligible;

            GlobalCacheServiceConfiguration = Create(() => CreateGlobalCacheServiceConfiguration());

            GlobalCacheService = CreateOptional(
                () => isGlobalCacheServiceEnabled,
                () => CreateGlobalCacheService());

            GlobalCacheCheckpointManager = Create(() => CreateGlobalCacheCheckpointManager());

            GlobalCacheStreamStorage = Create(() => CreateGlobalCacheStreamStorage());

            RocksDbContentMetadataStore = Create(() => CreateRocksDbContentMetadataStore());

            RoleObserver = CreateOptional<IRoleObserver>(
                () => isGlobalCacheServiceEnabled && GlobalCacheService.InstanceOrDefault() is ResilientGlobalCacheService,
                () => (ResilientGlobalCacheService)GlobalCacheService.InstanceOrDefault());

            ContentLocationStoreServices = Create(() => ContentLocationStoreFactory.Instance.Services);

            ContentLocationStoreFactory = Create(() =>
            {
                return new ContentLocationStoreFactory(
                    new ContentLocationStoreFactoryArguments()
                    {
                        Clock = Arguments.Clock,
                        Copier = Arguments.DistributedContentCopier,
                        ConnectionMap = Arguments.ConnectionMap,
                        PreferredContentStore = Arguments.PreferredContentStore,
                        Dependencies = new ContentLocationStoreServicesDependencies()
                        {
                            GlobalCacheService = GlobalCacheService.UnsafeGetServiceDefinition().AsOptional<IGlobalCacheService>(),
                            RoleObserver = RoleObserver,
                            DistributedContentSettings = CreateOptional(() => true, () => DistributedContentSettings),
                            GlobalCacheCheckpointManager = GlobalCacheCheckpointManager.AsOptional()
                        },
                    },
                    Arguments.ContentLocationStoreConfiguration);
            });

            CacheServiceCheckpointRegistry = Create(() => CreateCacheServiceBlobCheckpointRegistry());
        }

        private GlobalCacheServiceConfiguration CreateGlobalCacheServiceConfiguration()
        {
            return new GlobalCacheServiceConfiguration()
            {
                EnableBackgroundRestoreCheckpoint = DistributedContentSettings.GlobalCacheBackgroundRestore,
                MaxOperationConcurrency = DistributedContentSettings.MetadataStoreMaxOperationConcurrency,
                MaxOperationQueueLength = DistributedContentSettings.MetadataStoreMaxOperationQueueLength,
                CheckpointMaxAge = DistributedContentSettings.ContentMetadataCheckpointMaxAge?.Value,
                MaxEventParallelism = ContentLocationStoreConfiguration.EventStore.MaxEventProcessingConcurrency,
                MasterLeaseStaleThreshold = DateTimeUtilities.Multiply(ContentLocationStoreConfiguration.Checkpoint.MasterLeaseExpiryTime, 0.5),
                PersistentEventStorage = new BlobEventStorageConfiguration()
                {
                    Credentials = Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.ContentMetadataBlobSecretName }).First(),
                    FolderName = "events" + DistributedContentSettings.KeySpacePrefix,
                    ContainerName = DistributedContentSettings.ContentMetadataLogBlobContainerName,
                    StorageInteractionTimeout = DistributedContentSettings.ContentMetadataLogStorageInteractionTimeout
                },
                CentralStorage = ContentLocationStoreConfiguration.CentralStore with
                {
                    ContainerName = DistributedContentSettings.ContentMetadataCentralStorageContainerName
                },
                EventStream = new ContentMetadataEventStreamConfiguration()
                {
                    ShutdownTimeout = DistributedContentSettings.ContentMetadataShutdownTimeout,
                    LogBlockRefreshInterval = DistributedContentSettings.ContentMetadataPersistInterval,
                    MinWriteAheadInterval = DistributedContentSettings.ContentMetadataMaxWriteAheadInterval
                },
                Checkpoint = ContentLocationStoreConfiguration.Checkpoint with
                {
                    WorkingDirectory = Arguments.PrimaryCacheRoot / "cmschkpt",
                    RestoreCheckpointInterval = DistributedContentSettings.GlobalCacheRestoreCheckpointInterval ?? ContentLocationStoreConfiguration.Checkpoint.RestoreCheckpointInterval,
                    CreateCheckpointInterval = DistributedContentSettings.GlobalCacheCreateCheckpointInterval ?? ContentLocationStoreConfiguration.Checkpoint.CreateCheckpointInterval
                },
            };
        }

        internal AzureBlobStorageCheckpointRegistry CreateCacheServiceBlobCheckpointRegistry()
        {
            var clock = Arguments.Clock;

            var azureStorageCredentials = Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.ContentMetadataBlobSecretName }).First();
            var configuration = new AzureBlobStorageCheckpointRegistryConfiguration
            {
                Storage = new AzureBlobStorageCheckpointRegistryConfiguration.StorageSettings(Credentials: azureStorageCredentials)
                {
                    FolderName = "checkpointRegistry" + DistributedContentSettings.KeySpacePrefix,
                    ContainerName = DistributedContentSettings.ContentMetadataBlobCheckpointRegistryContainerName,
                },
                KeySpacePrefix = DistributedContentSettings.KeySpacePrefix,
            };

            var storageRegistry = new AzureBlobStorageCheckpointRegistry(configuration, clock);
            storageRegistry.WorkaroundTracer = new Tracer("ContentMetadataAzureBlobStorageCheckpointRegistry");
            return storageRegistry;
        }

        private CentralStreamStorage CreateGlobalCacheStreamStorage()
        {
            var configuration = GlobalCacheServiceConfiguration.Instance;
            return configuration.CentralStorage.CreateCentralStorage();
        }

        private RocksDbContentMetadataStore CreateRocksDbContentMetadataStore()
        {
            var configuration = GlobalCacheServiceConfiguration.Instance;
            var clock = Arguments.Clock;
            var primaryCacheRoot = Arguments.PrimaryCacheRoot;

            var dbConfig = new RocksDbContentMetadataDatabaseConfiguration(primaryCacheRoot / "cms")
            {
                // Setting to false, until we have persistence for the db
                CleanOnInitialize = false,
                MetadataSizeRotationThreshold = DistributedContentSettings.GlobalCacheMetadataSizeRotationThreshold,
            };

            ApplyIfNotNull(DistributedContentSettings.ContentMetadataDatabaseRocksDbTracingLevel, v => dbConfig.RocksDbTracingLevel = (RocksDbSharp.LogLevel)v);
            ApplyIfNotNull(DistributedContentSettings.LocationEntryExpiryMinutes, v => dbConfig.ContentRotationInterval = TimeSpan.FromMinutes(v));
            dbConfig.MetadataRotationInterval = DistributedContentSettings.ContentMetadataServerMetadataRotationInterval;

            var store = new RocksDbContentMetadataStore(
                clock,
                new RocksDbContentMetadataStoreConfiguration()
                {
                    DisableRegisterLocation = DistributedContentSettings.ContentMetadataDisableDatabaseRegisterLocation,
                    Database = dbConfig,
                });

            return store;
        }

        private CheckpointManager CreateGlobalCacheCheckpointManager()
        {
            var configuration = GlobalCacheServiceConfiguration.Instance;
            var clock = Arguments.Clock;

            CentralStorage centralStorage = GlobalCacheStreamStorage.Instance;

            if (ContentLocationStoreConfiguration.DistributedCentralStore != null)
            {
                var metadataConfig = ContentLocationStoreConfiguration.DistributedCentralStore with
                {
                    CacheRoot = configuration.Checkpoint.WorkingDirectory
                };

                if (!GlobalCacheService.IsAvailable
                    && DistributedContentSettings.GlobalCacheDatabaseValidationMode != DatabaseValidationMode.None
                    && DistributedContentSettings.GlobalCacheBackgroundRestore)
                {
                    // Restore checkpoints in checkpoint manager when GCS is unavailable since
                    // GCS is the normal driver of checkpoint restore
                    configuration.Checkpoint.RestoreCheckpoints = true;

                    // We can use LLS for checkpoint file distribution on GCS-INeligible machines
                    // On GCS-eligible machines there can be a cycle of GCS (RestoreCheckpoint) => LLS (GetBulkGlobal) => GCS
                    var dcs = new DistributedCentralStorage(
                        metadataConfig,
                        new DistributedCentralStorageLocationStoreAdapter(() => ContentLocationStoreServices.Instance.LocalLocationStore.Instance),
                        Arguments.DistributedContentCopier,
                        fallbackStorage: centralStorage,
                        clock,
                        Arguments.PreferredContentStore);

                    centralStorage = dcs;
                }
                else
                {
                    var cachingCentralStorage = new CachingCentralStorage(
                        metadataConfig,
                        centralStorage,
                        Arguments.DistributedContentCopier.FileSystem,
                        Arguments.PreferredContentStore);

                    centralStorage = cachingCentralStorage;
                }
            }

            var checkpointManager = new CheckpointManager(
                    RocksDbContentMetadataStore.Instance.Database,
                    CacheServiceCheckpointRegistry.Instance,
                    centralStorage,
                    configuration.Checkpoint,
                    new CounterCollection<ContentLocationStoreCounters>(),
                    clock);

            // This is done to ensure logging in Kusto is shown under a separate component. The need for this comes
            // from the fact that CheckpointManager per-se is used in our Kusto dashboards and monitoring queries to
            // mean "LLS' checkpoint"
            checkpointManager.WorkaroundTracer = new Tracer($"MetadataServiceCheckpointManager");

            return checkpointManager;
        }

        private GlobalCacheService CreateGlobalCacheService()
        {
            var configuration = GlobalCacheServiceConfiguration.Instance;
            var clock = Arguments.Clock;

            var volatileConfig = configuration.PersistentEventStorage with
            {
                Credentials = DistributedContentSettings.GlobalCacheWriteAheadBlobSecretName != null
                    ? Arguments.Secrets.GetStorageCredentials(new[] { DistributedContentSettings.GlobalCacheWriteAheadBlobSecretName })[0]
                    : configuration.PersistentEventStorage.Credentials,
                ContainerName = "volatileeventstorage"
            };

            var writeAheadEventStorage = new BlobWriteAheadEventStorage(volatileConfig);
            var eventStream = new ContentMetadataEventStream(
                    configuration.EventStream,
                    writeAheadEventStorage);

            var checkpointManager = GlobalCacheCheckpointManager.Instance;
            var service = new ResilientGlobalCacheService(
                configuration,
                checkpointManager,
                RocksDbContentMetadataStore.Instance,
                eventStream,
                clock);

            return service;
        }
    }
}
