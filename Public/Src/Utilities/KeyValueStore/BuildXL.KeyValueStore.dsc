// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed  from "Sdk.Managed";

namespace KeyValueStore {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.KeyValueStore",
        generateLogs: false,
        sources: [
            ...globR(d`.`, "*.cs"),
        ],
        nullable: true,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.IO.dll,
                NetFx.System.Text.Encoding.dll
            ),
            $.dll,
            Native.dll,
            Utilities.Core.dll,
            ...importFrom("Sdk.Selfhost.RocksDbSharp").pkgs,
        ],
    });
}
