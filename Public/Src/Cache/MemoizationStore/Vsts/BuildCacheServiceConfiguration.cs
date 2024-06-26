// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using Newtonsoft.Json;

// ReSharper disable MemberCanBePrivate.Global
namespace BuildXL.Cache.MemoizationStore.Vsts
{
    /// <summary>
    /// Represents a data class that contains configuration data for a VSTS Build Cache Service.
    /// </summary>
    [DataContract]
    public class BuildCacheServiceConfiguration : PublishingCacheConfiguration
    {
        /// <summary>
        /// Gets or sets the number of days to keep content before it is referenced by metadata.
        /// </summary>
        public const int DefaultDaysToKeepUnreferencedContent = 1;

        /// <summary>
        /// Gets or sets the threshold to inline pin calls instead of doing them in the background.
        /// </summary>
        public const int DefaultPinInlineThresholdMinutes = 15;

        /// <summary>
        /// Gets or sets the threshold to ignore pin calls.
        /// </summary>
        public const int DefaultIgnorePinThresholdHours = 16;

        /// <summary>
        /// Default minimum number of days to keep content bags and referenced content.
        /// </summary>
        public const int DefaultDaysToKeepContentBags = 7;

        /// <summary>
        /// Default the range of additional days of expiry to be added to the expiration of content bags and referenced content.
        /// </summary>
        public const int DefaultRangeOfDaysToKeepContentBags = 2;

        /// <summary>
        /// Default value indicating whether the client talking to the
        /// service is doing so in a read-only manner or if it can allow writes into the cache.
        /// </summary>
        public const bool DefaultIsCacheServiceReadOnly = false;

        /// <summary>
        /// Default number of selectors to fetch from the remote service.
        /// </summary>
        public const int DefaultMaxFingerprintSelectorsToFetch = 500;

        /// <summary>
        /// Gets or sets the namespace of the cache being communicated with.
        /// </summary>
        public const string DefaultCacheNamespace = BuildCacheResourceIds.DefaultCacheNamespace;

        /// <summary>
        /// Default value indicating whether the client should attempt to seal unbacked ContentHashLists.
        /// </summary>
        public const bool DefaultSealUnbackedContentHashLists = false;

        /// <summary>
        /// Default value indicating whether or not to use the Production AAD SPS instance for authentication
        /// when connecting to the VSTS accounts specified in the config.
        /// </summary>
        public const bool DefaultUseAad = true;

        /// <summary>
        /// Default value indicating whether or not blob based metadata entries are used in the VSTS cache.
        /// </summary>
        public const bool DefaultUseBlobContentHashLists = false;

        /// <summary>
        /// Default value indicating whether strong fingerprints will be incorporated.
        /// This is a feature flag.
        /// </summary>
        public const bool DefaultIncorporationEnabled = false;

        /// <summary>
        /// Default max fingerprints allowed per chunk.
        /// </summary>
        public const int DefaultMaxFingerprintsPerIncorporateRequest = 500;

        /// <summary>
        /// Default number of minutes to wait for a response after sending an http request before timing out.
        /// </summary>
        public const int DefaultHttpSendTimeoutMinutes = 5;

        /// <summary>
        /// Default value indicating whether Dedup is enabled.
        /// </summary>
        public const bool DefaultUseDedupStore = false;

        /// <summary>
        /// Default value indicating whether implicit pin is used.
        /// </summary>
        public const ImplicitPin DefaultImplicitPin = ImplicitPin.PutAndGet;

        /// <summary>
        /// Default value indicating whether Unix file access mode override is enabled.
        /// </summary>
        public const bool DefaultOverrideUnixFileAccessMode = false;

        /// <summary>
        /// Default value indicating whether eager fingerprint incorporation is enabled.
        /// </summary>
        public const bool DefaultEnableEagerFingerprintIncorporation = false;

        /// <nodoc />
        public const int DefaultEagerFingerprintIncorporationNagleIntervalMinutes = 5;

        /// <nodoc />
        public const int DefaultEagerFingerprintIncorporationNagleBatchSize = 100;

        /// <nodoc />
        public static TimeSpan DefaultEagerFingerprintIncorporationExpiry = TimeSpan.FromDays(1);

        /// <nodoc />
        public const int DefaultInlineFingerprintIncorporationExpiryHours = 8;

        /// <nodoc />
        public const byte DefaultDomainId = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheServiceConfiguration"/> class.
        /// </summary>
        [JsonConstructor]
        public BuildCacheServiceConfiguration()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildCacheServiceConfiguration"/> class.
        /// </summary>
        public BuildCacheServiceConfiguration(string cacheServiceFingerprintEndpoint, string cacheServiceContentEndpoint)
            : this()
        {
            CacheServiceContentEndpoint = cacheServiceContentEndpoint;
            CacheServiceFingerprintEndpoint = cacheServiceFingerprintEndpoint;
        }

        /// <summary>
        /// Gets the endpoint to talk to the fingerprint controller of a Build Cache Service.
        /// </summary>
        [DataMember]
        public string CacheServiceFingerprintEndpoint { get; set; }

        /// <summary>
        /// Gets the endpoint to talk to the content management controller of a Build Cache Service.
        /// </summary>
        [DataMember]
        public string CacheServiceContentEndpoint { get; set; }

        /// <summary>
        /// Gets or sets the number of days to keep content before it is referenced by metadata.
        /// </summary>
        [DataMember]
        public int DaysToKeepUnreferencedContent { get; set; } = DefaultDaysToKeepUnreferencedContent;

        /// <summary>
        /// Gets or sets the number of days to keep content before it is referenced by metadata.
        /// </summary>
        [DataMember]
        public int PinInlineThresholdMinutes { get; set; } = DefaultPinInlineThresholdMinutes;

        /// <summary>
        /// Gets or sets the number of hours to keep content before it is referenced by metadata.
        /// </summary>
        [DataMember]
        public int IgnorePinThresholdHours { get; set; } = DefaultIgnorePinThresholdHours;

        /// <summary>
        /// Gets or sets required amount of time content is required to be persisted to satisfy pin.
        /// </summary>
        [DataMember]
        public double RequiredContentKeepUntilHours { get; set; } = -1;

        /// <summary>
        /// Gets or sets the minimum number of days to keep content bags and referenced content.
        /// </summary>
        [DataMember]
        public int DaysToKeepContentBags { get; set; } = DefaultDaysToKeepContentBags;

        /// <summary>
        /// Gets or sets the range of additional days of expiry to be added to the expiration of content bags and referenced content.
        /// </summary>
        /// <remarks>
        /// Since
        /// 1) the extra expiration is chosen randomly within this range and
        /// 2) a value's expiration only needs to be updated if it has fallen below the minimum,
        /// on average the update must only be sent twice during each of these intervals.
        /// </remarks>
        [DataMember]
        public int RangeOfDaysToKeepContentBags { get; set; } = DefaultRangeOfDaysToKeepContentBags;

        /// <summary>
        /// Gets or sets a value indicating whether the client talking to the
        /// service is doing so in a read-only manner or if it can allow writes into the cache.
        /// </summary>
        [DataMember]
        public bool IsCacheServiceReadOnly { get; set; } = DefaultIsCacheServiceReadOnly;

        /// <summary>
        /// Gets or sets the number of selectors to fetch from the remote service.
        /// </summary>
        [DataMember]
        public int MaxFingerprintSelectorsToFetch { get; set; } = DefaultMaxFingerprintSelectorsToFetch;

        /// <summary>
        /// Gets or sets the namespace of the cache being communicated with. Default: "default"
        /// </summary>
        [DataMember]
        public string CacheNamespace { get; set; } = DefaultCacheNamespace;

        /// <summary>
        /// Gets or sets a value indicating whether the client should attempt to seal unbacked ContentHashLists.
        /// </summary>
        [DataMember]
        public bool SealUnbackedContentHashLists { get; set; } = DefaultSealUnbackedContentHashLists;

        /// <summary>
        /// Gets or sets a value indicating whether or not to use the Production AAD SPS instance for authentication
        /// when connecting to the VSTS accounts specified in the config.
        /// </summary>
        [DataMember]
        public bool UseAad { get; set; } = DefaultUseAad;

        /// <summary>
        /// Gets or sets a value indicating whether or not blob based metadata entries are used in the VSTS cache.
        /// </summary>
        [DataMember]
        public bool UseBlobContentHashLists { get; set; } = DefaultUseBlobContentHashLists;

        /// <summary>
        /// Gets or sets a value indicating whether strong fingerprints will be incorporated.
        /// This is a feature flag. Default: Disabled
        /// </summary>
        [DataMember]
        public bool FingerprintIncorporationEnabled { get; set; } = DefaultIncorporationEnabled;

        /// <summary>
        /// Gets or sets the maximum number of fingerprints chunks sent in parallel. Default: System.Environment.ProcessorCount
        /// </summary>
        [DataMember]
        public int MaxDegreeOfParallelismForIncorporateRequests { get; set; } = System.Environment.ProcessorCount;

        /// <summary>
        /// Gets or sets the max fingerprints allowed per chunk. Default: 500
        /// </summary>
        [DataMember]
        public int MaxFingerprintsPerIncorporateRequest { get; set; } = DefaultMaxFingerprintsPerIncorporateRequest;

        /// <summary>
        /// Gets or sets the number of minutes to wait for a response after sending an http request before timing out. Default: 5 minutes
        /// </summary>
        [DataMember]
        public int HttpSendTimeoutMinutes { get; set; } = DefaultHttpSendTimeoutMinutes;

        /// <summary>
        /// Gets or sets whether Dedup is enabled.
        /// </summary>
        [DataMember]
        public bool UseDedupStore { get; set; } = DefaultUseDedupStore;

        /// <summary>
        /// Gets or sets whether an implicit pin is used.
        /// </summary>
        [DataMember]
        public ImplicitPin ImplicitPin { get; set; } = DefaultImplicitPin;

        /// <summary>
        /// Gets or sets whether to override Unix file access modes.
        /// </summary>
        [DataMember]
        public bool OverrideUnixFileAccessMode { get; set; } = DefaultOverrideUnixFileAccessMode;

        /// <summary>
        /// Gets or sets whether eager fingerprint incorporation is enabled.
        /// </summary>
        /// <remarks>
        /// if the flag is set, then fingerprints will be incorporated (i.e. the ttl will be updated) eagerly within
        /// the operations that gets or touches the fingerprints (like GetContentHashList).
        ///
        /// Currently we have 3 ways for fingerprint incorporation:
        /// 1. Inline incorporation: If eager fingerprint incorporation enabled (<see cref="EnableEagerFingerprintIncorporation"/> is true) and
        ///                          the entry will expire in <see cref="InlineFingerprintIncorporationExpiryHours"/> time.
        /// 2. Eager bulk incorporation: if eager fingerprint incorporation enabled (<see cref="EnableEagerFingerprintIncorporation"/> is true) and
        ///                          the entry's expiry is not available or it won't expire in <see cref="EnableEagerFingerprintIncorporation"/> time.
        /// 3. Session shutdown incorporation: if eager fingerprint incorporation is disabled and the normal fingerprint incorporation is enabled (<see cref="FingerprintIncorporationEnabled"/> is true).
        /// </remarks>
        [DataMember]
        public bool EnableEagerFingerprintIncorporation { get; set; } = DefaultEnableEagerFingerprintIncorporation;

        /// <summary>
        /// Gets or sets time window during which incorporation is done inline.
        /// </summary>
        [DataMember]
        public long InlineFingerprintIncorporationExpiryHours { get; set; } = DefaultInlineFingerprintIncorporationExpiryHours;

        /// <nodoc />
        [DataMember]
        public int EagerFingerprintIncorporationNagleIntervalMinutes { get; set; } = DefaultEagerFingerprintIncorporationNagleIntervalMinutes;

        /// <nodoc />
        [DataMember]
        public int EagerFingerprintIncorporationNagleBatchSize { get; set; } = DefaultEagerFingerprintIncorporationNagleBatchSize;

        /// <nodoc />
        [DataMember]
        public byte DomainId { get; set; } = DefaultDomainId;

        /// <summary>
        /// Gets whether basic HttpClient is used with downloading blobs from Azure blob store
        /// as opposed to using Azure Storage SDK.
        /// </summary>
        /// <remarks>
        /// There are known issues with timeouts, hangs, unobserved exceptions in the Azure
        /// Storage SDK, so this is provided as potentially permanent workaround by performing
        /// downloads using basic http requests.
        /// </remarks>
        [DataMember]
        public bool DownloadBlobsUsingHttpClient { get; set; } = Environment.GetEnvironmentVariable("BUILD_CACHE_BLOB_USE_HTTPCLIENT") != "0";

        /// <nodoc />
        [DataMember]
        public bool ForceUpdateOnAddContentHashList { get; set; } = false;

        /// <summary>
        /// Whether to request URIs from L3 when retreiving CHLs.
        /// </summary>
        [DataMember]
        public bool IncludeDownloadUris { get; set; } = true;
    }
}
