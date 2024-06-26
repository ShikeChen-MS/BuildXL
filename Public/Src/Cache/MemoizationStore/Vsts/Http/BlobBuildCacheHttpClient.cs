// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.VstsInterfaces.Blob;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildXL.Cache.MemoizationStore.Vsts.Http
{
    /// <summary>
    /// A concrete implementation of an http client communicating with the VSTS build cache service.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
    [ResourceArea(BuildCacheResourceIds.BuildCacheArea)]
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class BlobBuildCacheHttpClient : VssHttpClientBase, IArtifactBlobBuildCacheHttpClient, IAdminBuildCacheHttpClient
    {
        private static readonly Dictionary<string, Type> TranslatedExceptionsValue = new Dictionary<string, Type>
            {
                { "ContentBagNotFoundException", typeof(ContentBagNotFoundException) }
            };

        private const string IncludeDownloadUrisParamName = "includeDownloadUris";

        private static readonly IEnumerable<KeyValuePair<string, string>> IncludeDownloadUrisParams = new Dictionary<string, string>
            {
                { IncludeDownloadUrisParamName, "true" }
            };

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheHttpClient"/> class.
        /// </summary>
        public BlobBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials)
            : base(baseUrl, credentials)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheHttpClient"/> class.
        /// </summary>
        public BlobBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, handlers)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheHttpClient"/> class.
        /// </summary>
        public BlobBuildCacheHttpClient(
            Uri baseUrl,
            VssCredentials credentials,
            VssHttpRequestSettings requestSettings,
            params DelegatingHandler[] handlers)
            : base(baseUrl, credentials, requestSettings, handlers)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobBuildCacheHttpClient"/> class.
        /// </summary>
        public BlobBuildCacheHttpClient(
            Uri baseUrl,
            HttpMessageHandler pipeline,
            bool disposeHandler)
            : base(baseUrl, pipeline, disposeHandler)
        {
        }

        /// <summary>
        /// Gets the exceptions for errors
        /// </summary>
        protected override IDictionary<string, Type> TranslatedExceptions => TranslatedExceptionsValue;

        /// <inheritdoc />
        public async Task<Guid> ResetBuildCacheServiceDeterminism(string cacheNamespace, Guid existingCacheDeterminism)
        {
            CacheDeterminismResponse determinismGuid =
                await PostAsync<ResetCacheDeterminismRequest, CacheDeterminismResponse>(
                    new ResetCacheDeterminismRequest(existingCacheDeterminism),
                    BuildCacheResourceIds.CacheDeterminismGuidResourceId);

            return determinismGuid.CacheDeterminism;
        }

        /// <inheritdoc />
        public Task GetOptionsAsync(CancellationToken cancellationToken)
        {
            // ReSharper disable once MethodSupportsCancellation
            return SendAsync(HttpMethod.Options, BuildCacheResourceIds.BlobContentHashListResourceId, cancellationToken);
        }

        /// <inheritdoc />
        public Task<BlobContentHashListResponse> GetContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            bool includeDownloadUris)
        {
            var routeValues = new
            {
                cacheNamespace,
                weakFingerprint = strongFingerprint.WeakFingerprint.ToHex(),
                selectorContentHash = strongFingerprint.Selector.ContentHash.ToHex(),
                selectorOutput = strongFingerprint.Selector.Output?.ToHex() ?? BuildCacheResourceIds.NoneSelectorOutput
            };

            return GetAsync<BlobContentHashListResponse>(
                BuildCacheResourceIds.BlobContentHashListResourceId,
                routeValues,
                queryParameters: includeDownloadUris ? IncludeDownloadUrisParams : null);
        }

        /// <inheritdoc />
        public Task<BlobContentHashListResponse> AddContentHashListAsync(
            string cacheNamespace,
            StrongFingerprint strongFingerprint,
            BlobContentHashListWithCacheMetadata contentHashList,
            bool forceUpdate)
        {
            var contentHashListParameters = new
            {
                cacheNamespace,
                weakFingerprint = strongFingerprint.WeakFingerprint.ToHex(),
                selectorContentHash = strongFingerprint.Selector.ContentHash.ToHex(),
                selectorOutput = strongFingerprint.Selector.Output?.ToHex() ?? BuildCacheResourceIds.NoneSelectorOutput,
            };

            var queryParameters = new Dictionary<string, string>();
            if (forceUpdate)
            {
                queryParameters["forceUpdate"] = forceUpdate.ToString();
            }

            return PostAsync<BlobContentHashListWithCacheMetadata, BlobContentHashListResponse>(
                contentHashList,
                BuildCacheResourceIds.BlobContentHashListResourceId,
                contentHashListParameters,
                queryParameters: queryParameters);
        }

        /// <inheritdoc />
        public Task<BlobSelectorsResponse> GetSelectors(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            bool includeDownloadUris,
            int maxSelectorsToFetch)
        {
            var queryParameters = new Dictionary<string, string>
            {
                { "maxSelectorsToFetch", maxSelectorsToFetch.ToString() }
            };
            return GetSelectorsInternal(cacheNamespace, weakFingerprint, includeDownloadUris, queryParameters);
        }

        /// <inheritdoc />
        public Task<BlobSelectorsResponse> GetSelectors(string cacheNamespace, Fingerprint weakFingerprint, bool includeDownloadUris)
        {
            var queryParameters = new Dictionary<string, string>();
            return GetSelectorsInternal(cacheNamespace, weakFingerprint, includeDownloadUris, queryParameters);
        }

        /// <inheritdoc />
        public Task IncorporateStrongFingerprints(
            string cacheNamespace,
            IncorporateStrongFingerprintsRequest incorporateRequest)
        {
            var routeValues = new
            {
                cacheNamespace,
            };

            var queryParameters = new Dictionary<string, string>
            {
                { "useBlobContentHashlists", "true" }
            };

            return PutAsync(
                incorporateRequest,
                BuildCacheResourceIds.IncorporateStrongFingerprintsResourceId,
                routeValues,
                queryParameters: queryParameters);
        }

        /// <inheritdoc />
        public async Task<Guid> GetBuildCacheServiceDeterminism(string cacheNamespace)
        {
            CacheDeterminismResponse cacheDeterminismResponse = await GetAsync<CacheDeterminismResponse>(
                BuildCacheResourceIds.CacheDeterminismGuidResourceId,
                new {cacheNamespace });
            return cacheDeterminismResponse.CacheDeterminism;
        }

        private Task<BlobSelectorsResponse> GetSelectorsInternal(
            string cacheNamespace,
            Fingerprint weakFingerprint,
            bool includeDownloadUris,
            Dictionary<string, string> queryParameters)
        {
            if (includeDownloadUris)
            {
                queryParameters[IncludeDownloadUrisParamName] = "true";
            }

            return GetAsync<BlobSelectorsResponse>(
                BuildCacheResourceIds.BlobSelectorResourceId,
                new {cacheNamespace, weakFingerprint = weakFingerprint.ToHex() },
                null,
                queryParameters);
        }

        private IAppTraceSource _tracer;

        /// <inheritdoc />
        public void SetTracer(IAppTraceSource tracer)
        {
            if (_tracer != null)
            {
                throw new InvalidOperationException($"{nameof(SetTracer)} was already called earlier. In order to preserve thread safety it cannot be changed.");
            }

            _tracer = tracer;
        }
    }
}
