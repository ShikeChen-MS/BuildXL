// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildXL.Cache.BuildCacheResource.Model
{
    /// <summary>
    /// The configuration of a 1ES build cache
    /// </summary>
    public record BuildCacheConfiguration
    {
        private readonly int _retentionPolicyInDays;
        private readonly IReadOnlyCollection<BuildCacheShard>? _shards;

        /// <summary>
        /// The logical name of the cache resource, as defined by the user
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// The number of days metadata will be evicted if not accessed. Only relevant in the context of service-less GC.
        /// </summary>
        /// <remarks>
        /// This property may become optional in the future
        /// </remarks>
        [JsonPropertyName("RetentionDays")]
        public required int RetentionPolicyInDays
        {
            get => _retentionPolicyInDays;
            init
            {
                // This restriction may be lifted in the future. The current version of the cache resource will use service-less GC and is expected
                // to always communicate a positive value for the retention policy
                if (value! <= 0)
                {
                    throw new ArgumentException($"The retention policy for the cache must be a positive value.", nameof(RetentionPolicyInDays));
                }

                _retentionPolicyInDays = value;
            }
        }

        /// <summary>
        /// The blob storage accounts (shards) that back the resource
        /// </summary>
        public required IReadOnlyCollection<BuildCacheShard> Shards
        {
            get => _shards!;
            init
            {
                if (value.Count == 0)
                {
                    throw new ArgumentException($"The number of shards for the cache must be a positive value.", nameof(Shards));
                }

                _shards = value;
            }
        }
    }
}
