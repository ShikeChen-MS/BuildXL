// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
{
    /// <summary>
    /// Performs remote pinning for <see cref="IContentSession.PinAsync(BuildXL.Cache.ContentStore.Interfaces.Tracing.Context, System.Collections.Generic.IReadOnlyList{BuildXL.Cache.ContentStore.Hashing.ContentHash}, CancellationToken, UrgencyHint)"/>
    /// </summary>
    public delegate Task<IEnumerable<Task<Indexed<PinResult>>>> RemotePinAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            bool succeedWithOneLocation,
            UrgencyHint urgencyHint = UrgencyHint.Nominal
        );
}
