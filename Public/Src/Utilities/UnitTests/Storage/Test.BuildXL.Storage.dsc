// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

namespace Storage {
    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.BuildXL.Storage",
        allowUnsafeBlocks: true,
        assemblyBindingRedirects: BuildXLSdk.cacheBindingRedirects(),
        runTestArgs: {
            unsafeTestRunArguments: {
                // when run as admin, tests leave some files around that causes qtest to fail with
                // System.IO.IOException: The data present in the reparse point buffer is invalid.
                forceXunitForAdminTests: true
            }
        },
        sources: globR(d`.`, "*.cs"),
        references: [
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Native.Extensions.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            ...importFrom("BuildXL.Utilities").Native.securityDlls,

            ...BuildXLSdk.systemMemoryDeployment,
        ],
        runtimeContent: [
            dummyWaiterExe
        ],
    });
}
