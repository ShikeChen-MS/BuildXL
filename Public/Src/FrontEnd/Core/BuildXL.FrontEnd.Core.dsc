// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { NetFx } from "Sdk.BuildXL";
import * as BuildXLSdk from "Sdk.BuildXL";
import * as Managed from "Sdk.Managed";

namespace Core {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.Core",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        addNotNullAttributeFile: true,
        references: [
            ...addIf(BuildXLSdk.isFullFramework,
                NetFx.System.Net.Http.dll,
                NetFx.System.Web.dll
            ),
            importFrom("BuildXL.Cache.ContentStore").Hashing.dll,
            importFrom("BuildXL.Cache.ContentStore").UtilitiesCore.dll,
            importFrom("BuildXL.Cache.ContentStore").Interfaces.dll,
            importFrom("BuildXL.Cache.ContentStore").Library.dll,
            importFrom("BuildXL.Cache.MemoizationStore").Interfaces.dll,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").ToolSupport.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("BuildXL.Utilities.Instrumentation").AriaCommon.dll,
            importFrom("Newtonsoft.Json").pkg,
            Sdk.dll,
            TypeScript.Net.dll,

            ...BuildXLSdk.tplPackages,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,

            ...importFrom("BuildXL.Cache.ContentStore").getProtobufPackages(),
        ],
        internalsVisibleTo: [
            "bxlScriptAnalyzer",
            "BxlPipGraphFragmentGenerator",
            "Test.BuildXL.FrontEnd.Script",
            "Test.BuildXL.FrontEnd.Download",
            "Test.BuildXL.FrontEnd.Core",
        ],
    });
}
