import * as Shared from "Sdk.Managed.Shared";

namespace WebFramework {
    
    export declare const qualifier : Shared.TargetFrameworks.CoreClr;

    @@public
    export function getFrameworkPackage() : Shared.ManagedNugetPackage {
        Contract.assert(isDotNetCore);
        return Shared.Factory.createFrameworkPackage(
            importPackage(
                () => importFrom("Microsoft.AspNetCore.App.Ref.8.0.0").pkg,
                () => importFrom("Microsoft.AspNetCore.App.Ref.9.0.0").pkg),
            getRuntimePackage(),
            a`${qualifier.targetRuntime}`,
            a`${qualifier.targetFramework}`
        );
    }

    function getRuntimePackage() : Shared.ManagedNugetPackage {
        switch (qualifier.targetRuntime) {
            case "win-x64":
                return importPackage(
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.win-x64.8.0.0").pkg,
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.win-x64.9.0.0").pkg);
            case "osx-x64":
                return importPackage(
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64.8.0.0").pkg,
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.osx-x64.9.0.0").pkg);
            case "linux-x64":
                return importPackage(
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64.8.0.0").pkg,
                    () => importFrom("Microsoft.AspNetCore.App.Runtime.linux-x64.9.0.0").pkg);
            default:
                Contract.fail("Unsupported target framework");
        }
    }

    function importPackage(net80: () => Shared.ManagedNugetPackage, net90: () => Shared.ManagedNugetPackage) : Shared.ManagedNugetPackage {
        switch (qualifier.targetFramework) {
            case "net8.0": return net80();
            case "net9.0": return net90();
            default: Contract.fail(`Unsupported target framework ${qualifier.targetFramework}.`);
        }
    }
}