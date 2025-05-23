// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

export declare const qualifier : AllSupportedQualifiers;

/**
 * Qualifier that only supports full targetFramework
 */
@@public
export interface FullFrameworkQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net472";
    targetRuntime: "win-x64"
}

/**
 * Qualifier for projects that support DotNetCore
 */
@@public
export interface DefaultQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0" | "net9.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore and .NET472
 */
@@public
export interface DefaultQualifierWithNet472 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0" | "net9.0" | "net472";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Qualifier for projects that support DotNetCore, .NET472 and NetStandard2.0
 */
@@public
export interface AllSupportedQualifiers extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0" | "net9.0" | "net472" | "netstandard2.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Extension of the default qualifier with a platform
 */
@@public
export interface PlatformDependentQualifier extends Qualifier {
    platform: "x86" | "x64";
    configuration: "debug" | "release";
}

@@public
export interface Net8PlusQualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0" | "net9.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

@@public
export interface Net8Qualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

@@public
export interface Net9Qualifier extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net9.0";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * Having a net8 specific qualifier (without net6/net7) for some specific tests that only
 * work in net 8.
 * TODO: This should be consolidated with DefaultQualifier when we stop compiling for multiple /net versions.
 */
@@public
export interface Net8QualifierWithNet472 extends Qualifier {
    configuration: "debug" | "release";
    targetFramework: "net8.0" | "net472";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
}

/**
 * LatestFullFrameworkQualifier, used to pin tool execution to a specific .NET Framework version
 */
@@public
export const LatestFullFrameworkQualifier : FullFrameworkQualifier = {
    configuration: qualifier.configuration,
    targetFramework: "net472",
    targetRuntime: "win-x64"
};
