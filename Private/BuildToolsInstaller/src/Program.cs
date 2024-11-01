﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine;
using BuildToolsInstaller.Utiltiies;

namespace BuildToolsInstaller
{
    internal sealed class Program
    {
        private const int ProgramNotRunExitCode = 21; // Just a random number to please the compiler

        /// <nodoc />
        public static async Task<int> Main(string[] arguments)
        {
            if (Environment.GetEnvironmentVariable("BuildToolsDownloaderDebugOnStart") == "1")
            {
                System.Diagnostics.Debugger.Launch();
            }

            var toolOption = new Option<BuildTool>(
                name: "--tool",
                description: "The tool to install.")
                { IsRequired = true };

            var toolsDirectoryOption = new Option<string?>(
                name: "--toolsDirectory",
                description: "The location where packages should be downloaded. Defaults to AGENT_TOOLSDIRECTORY if defined, or the working directory if not");

            var forceOption = new Option<bool>(
                name: "--force",
                description: "Forces download and installation (prevents tool caching being applied)");

            var configOption = new Option<string?>(
                 name: "--config",
                 description: "Specific tool installer configuration file.",
                 parseArgument: result =>
                 {
                     if (result.Tokens.Count() != 1)
                     {
                         result.ErrorMessage = "--toolsDirectory should be specified once";
                         return null;
                     }

                     var filePath = result.Tokens.Single().Value;
                     if (!File.Exists(filePath))
                     {
                         result.ErrorMessage = $"The specified config file path '{filePath}' does not exist";
                         return null;
                     }

                     return filePath;
                 });

            var rootCommand = new RootCommand("Build tools installer");
            rootCommand.AddOption(toolOption);
            rootCommand.AddOption(toolsDirectoryOption);
            rootCommand.AddOption(configOption);
            rootCommand.AddOption(forceOption);

            int returnCode = ProgramNotRunExitCode; // Make the compiler happy, we should assign every time
            rootCommand.SetHandler(async (tool, toolsDirectory, configFile, forceInstallation) =>
            {
                toolsDirectory ??= AdoUtilities.ToolsDirectory ?? ".";
                returnCode = await BuildToolsInstaller.Run(new BuildToolsInstallerArgs()
                {
                    Tool = tool,
                    ToolsDirectory = toolsDirectory,
                    ConfigFilePath = configFile,
                    ForceInstallation = forceInstallation
                });
            },
                toolOption,
                toolsDirectoryOption,
                configOption,
                forceOption
            );

            await rootCommand.InvokeAsync(arguments);
            return returnCode;
        }
    }
}
