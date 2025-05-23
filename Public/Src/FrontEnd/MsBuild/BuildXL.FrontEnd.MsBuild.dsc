// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as BuildXLSdk from "Sdk.BuildXL";
import * as Deployment from "Sdk.Deployment";
import * as Managed from "Sdk.Managed";
import { NetFx } from "Sdk.BuildXL";
import {Transformer} from "Sdk.Transformers";

namespace MsBuild {
    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.FrontEnd.MsBuild",
        generateLogs: true,
        sources: globR(d`.`, "*.cs"),
        references: [
            ...BuildXLSdk.tplPackages,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Engine").Processes.dll,
            importFrom("BuildXL.Pips").dll,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Storage.dll,
            importFrom("BuildXL.Utilities").Script.Constants.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities").Utilities.Core.dll,
            importFrom("Newtonsoft.Json").pkg,
            Utilities.dll,
            TypeScript.Net.dll,
            Script.dll,
            Core.dll,
            Serialization.dll,
            Sdk.dll,
            SdkProjectGraph.dll,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.FrontEnd.MsBuild",
        ],
        runtimeContent: [
            // CODESYNC: \Public\Src\IDE\VsCode\BuildXL.IDE.VsCode.dsc 
            // We exclude the VbCsCompiler from the VsCode extension to save space.
            {
                subfolder: r`tools/vbcslogger/net472`,
                contents: [importFrom("BuildXL.Tools").VBCSCompilerLogger
                    .withQualifier({ targetFramework: "net472" }).dll]
            },
            {
                subfolder: r`tools/vbcslogger/dotnetcore`,
                // CODESYNC: qualifier in Public\Src\Tools\VBCSCompilerLogger\VBCSCompilerLogger.dsc
                // TODO: Remove qualifier override once Net8QualifierWithNet472 is dealt with.
                contents: [importFrom("BuildXL.Tools").VBCSCompilerLogger
                    .withQualifier({ targetFramework: "net8.0" }).dll]
            },
            {
                subfolder: r`tools`,
                // For the dotnet case, we are only deploying the tool for net8
                // TODO: Remove condition when we stop building for other .net versions
                contents: [qualifier.targetFramework === "net9.0"
                    ? importFrom("BuildXL.Tools").MsBuildGraphBuilder.withQualifier({targetFramework: "net8.0"}).deployment
                    : importFrom("BuildXL.Tools").MsBuildGraphBuilder.deployment],
            }
        ]
    });
}
