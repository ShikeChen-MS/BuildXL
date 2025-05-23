// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using BuildXL.App.Tracing;
using BuildXL.Native.IO;
using BuildXL.Native.Processes;
using BuildXL.Storage;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using Strings = bxl.Strings;

namespace BuildXL
{
    /// <summary>
    /// bxl.exe entry point. Sometimes a new process runs actual build work; other times it is a client for an existing 'app server'.
    /// <see cref="BuildXLApp"/> is the 'app' itself which does actual work. <see cref="AppServer"/> is named pipe-accessible host for that app.
    /// An app server is effectively a cached
    /// instance of bxl.exe to amortize some startup overheads, whereas a 'listen mode' build engine (possibly running inside an app server) is a
    /// remotely controlled build (single graph, defined start and end point).
    /// </summary>
    internal sealed class Program
    {
        internal const string BuildXlAppServerConfigVariable = "BUILDXL_APP_SERVER_CONFIG";

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public static int Main(string[] rawArgs)
        {
            // TODO:#1208464 - this can be removed once BuildXL targets .net or newer 4.7 where TLS 1.2 is enabled by default
#pragma warning disable SYSLIB0014 // Type or member is obsolete. This setting is not used by HttpClient but it is
                                   // used by other classes (which are also obsolete starting Net9) that are present
                                   // in our codebase.
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
#pragma warning restore SYSLIB0014 // Type or member is obsolete

            Program p = new Program(rawArgs);

            // Note that we do not wrap Run in a catch-all exception handler. If we did, then last-chance handling (i.e., an 'unhandled exception'
            // event) is neutered - but only for the main thread! Instead, we want to have a uniform last-chance handling method that does the
            // right telemetry / Windows Error Reporting magic as part of crashing (possibly into a debugger).
            // TODO: Promote the last-chance handler from BuildXLApp to here?
            return p.Run();
        }

        private string[] RawArgs { get; set; }

        private Program(string[] rawArgs)
        {
            RawArgs = rawArgs;
        }

        /// <summary>
        /// The core execution of the tool.
        /// </summary>
        /// <remarks>
        /// If you discover boilerplate in multiple implementations, add it to MainImpl, or add another inheritance hierarchy.
        /// </remarks>
        public int Run()
        {
            // We may have been started to be an app server. See StartAppServerProcess. If so, run as an app server (and expect no args).
            string startupParamsSerialized = Environment.GetEnvironmentVariable(BuildXlAppServerConfigVariable);
            if (startupParamsSerialized != null)
            {
                if (RawArgs.Length > 0)
                {
                    // TODO: Message
                    return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
                }

                AppServer.StartupParameters startupParameters = AppServer.StartupParameters.TryParse(startupParamsSerialized);
                if (startupParameters == null)
                {
                    return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
                }

                return ExitCode.FromExitKind(RunAppServer(startupParameters));
            }

            LightConfig lightConfig;

            if (!LightConfig.TryParse(RawArgs, out lightConfig) && lightConfig.Help == HelpLevel.None)
            {
                // If light config parsing failed, go through the full argument parser to collect & print the errors
                // it would catch.
                ICommandLineConfiguration config;
                Analysis.IgnoreResult(Args.TryParseArguments(RawArgs, new PathTable(), null, out config));
                HelpText.DisplayHelp(BuildXL.ToolSupport.HelpLevel.Verbose);
                return ExitCode.FromExitKind(ExitKind.InvalidCommandLine);
            }

            // RunInSubst is only supported on Windows. Otherwise ignored.
            if (OperatingSystemHelper.IsWindowsOS && lightConfig.RunInSubst)
            {
                return ExecuteRunInSubst(lightConfig, RawArgs);
            }
            
            // Not an app server; will either run fully within this process ('single instance') or start / connect to an app server.
            if (!lightConfig.NoLogo)
            {
                HelpText.DisplayLogo();
            }

            if (lightConfig.Help != HelpLevel.None)
            {
                // Need to cast here to convert from the configuration enum to the ToolSupoort enum. Their values
                // are manually kept in sync to avoid the additional dependency.
                HelpText.DisplayHelp((BuildXL.ToolSupport.HelpLevel)lightConfig.Help);

                return ExitCode.FromExitKind(ExitKind.BuildNotRequested);
            }

            // Optionally perform some special tasks related to server mode
            switch (lightConfig.Server)
            {
                case ServerMode.Kill:
                    ServerDeployment.KillServer(ServerDeployment.ComputeDeploymentDir(lightConfig.ServerDeploymentDirectory));
                    Console.WriteLine(Strings.App_ServerKilled);
                    return ExitCode.FromExitKind(ExitKind.BuildNotRequested);
            }

            ExitKind exitKind = lightConfig.Server != ServerMode.Disabled
                ? ConnectToAppServerAndRun(lightConfig, RawArgs)
                : RunSingleInstance(RawArgs);

            return ExitCode.FromExitKind(exitKind);
        }

        /// <summary>
        /// Launches RunInSubst.exe specifying B:\\ as the target subst and the specified source subst (or the config.
        /// dsc location if not specified), followed by bxl.exe with its associated arguments.
        /// </summary>
        /// <remarks>
        /// This execution always appends to bxl arguments the corresponding subst source and target. In addition, it overrides with 
        /// /RunInSubst-, so the launched process won't try to launch RunInSubst.exe again.
        /// </remarks>
        private int ExecuteRunInSubst(LightConfig lightConfig, string[] rawArgs)
        {
            // Use the substTarget specified, otherwise default to B:\
            var substTarget = string.IsNullOrEmpty(lightConfig.SubstTarget) ? "B:\\" : lightConfig.SubstTarget;

            // Use the subst source specified. Otherwise use the location of the main config file as the default
            string substSource = string.IsNullOrEmpty(lightConfig.SubstSource) ? Directory.GetParent(lightConfig.Config).FullName : lightConfig.SubstSource;

            string clientPath = AssemblyHelper.GetThisProgramExeLocation();
            // CODESYNC: keep in sync with bxl deployment, we are assuming RunInSubst.exe is deployed alongside bxl.exe
            string runInSubstPath = Path.Combine(Directory.GetParent(clientPath).FullName, "RunInSubst.exe");

            // Subst the executable path, as this will be used by BuildXL later without going through path translations
            var translator = new DirectoryTranslator();
            translator.AddTranslation(substSource, substTarget);
            translator.Seal();
            string clientPathSubst = translator.Translate(clientPath);

            // Launch bxl again via RunInSubst as a child process, with same arguments, but disable /runInSubst and specify subst target and source
            char substTargetDriveLetter = substTarget[0];
            string arguments = $"{substTargetDriveLetter}=\"{substSource}\" \"{clientPathSubst}\" {string.Join(" ", rawArgs)} /runInSubst- /substTarget:{substTarget} /substSource:\"{substSource}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = runInSubstPath,
                Arguments = arguments,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                UseShellExecute = false,
            };

            var process = new Process() { StartInfo = startInfo };

            process.Start();
            process.WaitForExit();

            return process.ExitCode;
        }

        private static ExitKind RunSingleInstance(IReadOnlyCollection<string> rawArgs, ServerModeStatusAndPerf? serverModeStatusAndPerf = null)
        {
            using (var args = new Args())
            {
                var pathTable = new PathTable();

                ICommandLineConfiguration configuration;
                ContentHashingUtilities.SetContentHasherIdlePoolSize(10);
                if (!args.TryParse(rawArgs.ToArray(), pathTable, out configuration))
                {
                    return ExitKind.InvalidCommandLine;
                }

                string clientPath = AssemblyHelper.GetThisProgramExeLocation();
                var rawArgsWithExe = new List<string>(rawArgs.Count + 1) { clientPath };
                rawArgsWithExe.AddRange(rawArgs);

                using (var app = new BuildXLApp(
                    new SingleInstanceHost(),
                    null,

                    // BuildXLApp will create a standard console.
                    configuration,
                    pathTable,
                    rawArgsWithExe,
                    null,
                    serverModeStatusAndPerf))
                {
                    Console.CancelKeyPress +=
                        (sender, eventArgs) =>
                        {
                            eventArgs.Cancel = !app.OnConsoleCancelEvent(isTermination: eventArgs.SpecialKey == ConsoleSpecialKey.ControlBreak);
                        };

                    return app.Run().ExitKind;
                }
            }
        }

        private static ExitKind ConnectToAppServerAndRun(LightConfig lightConfig, IReadOnlyList<string> rawArgs)
        {
            using (AppServer.Connection connection = AppServer.TryStartOrConnect(
                startupParameters => TryStartAppServerProcess(startupParameters),
                lightConfig,
                lightConfig.ServerDeploymentDirectory,
                out var serverModeStatusAndPerf,
                out var environmentVariablesToPass))
            {
                if (connection == null)
                {
                    // Connection failed; fall back to single instance.
                    return RunSingleInstance(rawArgs, serverModeStatusAndPerf);
                }
                else
                {
                    try
                    {
                        return connection.RunWithArgs(rawArgs, environmentVariablesToPass, serverModeStatusAndPerf);
                    }
                    catch (BuildXLException ex)
                    {
                        Console.Error.WriteLine(Strings.AppServer_TerminatingClient_PipeDisconnect, ex.Message);
                        return ExitKind.InternalError;
                    }
                }
            }
        }

        private static ExitKind RunAppServer(AppServer.StartupParameters startupParameters)
        {
            ExitKind exitKind;
            using (
                var server =
                    new AppServer(maximumIdleTime: new TimeSpan(hours: 0, minutes: startupParameters.ServerMaxIdleTimeInMinutes, seconds: 0)))
            {
                exitKind = server.Run(startupParameters);
            }

            Exception telemetryShutdownException;
            if (AriaV2StaticState.TryShutDown(out telemetryShutdownException) == AriaV2StaticState.ShutDownResult.Failure)
            {
                exitKind = ExitKind.InfrastructureError;
            }

            return exitKind;
        }

        private static Possible<Unit> TryStartAppServerProcess(AppServer.StartupParameters startupParameters)
        {
            // For simplicity, we clone the current environment block - but add one additional variable.
            // Some variables like PATH are needed for the server to even start. Others like the COMPlus_*
            // family and BuildXLDebugOnStart affect its startup behavior. However, note that each
            // client actually separately provides a replacement environment block to the server; these blocks
            // are intended to affect the pip graph, rather than server startup.
            var environment = new Dictionary<string, string>();
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                environment[(string)variable.Key] = (string)variable.Value;
            }

            environment[BuildXlAppServerConfigVariable] = startupParameters.ToString();

            // We create a 'detached' process, since this server process is supposed to live a long time.
            // - We don't inherit any handles (maybe the caller accidentally leaked a bunch of pipe handles into this client process).
            // - We allocate a new hidden console.
            // - We require breakaway from containing jobs (otherwise, someone might use it to wait on the server by accident).
            int newProcessId;
            int errorCode;
            var status = BuildXL.Native.Processes.ProcessUtilities.CreateDetachedProcess(
                commandLine: startupParameters.PathToProcess,
                environmentVariables: environment,

                // Explicitly use the directory of the server process as the working directory. This will later be reset
                // to whatever directory the client is in when it connects to the server process. Some directory is needed
                // here, but it is intentionally using the incorrect directory rather than something that looks correct
                // since this is a once-only set. The correct path needs to be set for each build.
                workingDirectory: Path.GetDirectoryName(startupParameters.PathToProcess),
                newProcessId: out newProcessId,
                errorCode: out errorCode);

            switch (status)
            {
                case CreateDetachedProcessStatus.Succeeded:
                    return Unit.Void;
                case CreateDetachedProcessStatus.JobBreakwayFailed:
                    return new Failure<string>("The server process could not break-away from a containing job");
                case CreateDetachedProcessStatus.ProcessCreationFailed:
                    return new NativeFailure(errorCode).Annotate("Failed to create a server process");
                default:
                    throw Contract.AssertFailure("Unhandled CreateDetachedProcessStatus");
            }
        }
    }

    /// <summary>
    /// Provides access to the entry point of the assembly without taking a dependency on
    /// OneBuildProgram and associated types
    /// </summary>
    internal static class EntryPoint
    {
        /// <summary>
        /// Calls entrypoint of the assembly
        /// </summary>
        public static int Run(string[] args)
        {
            return Program.Main(args);
        }
    }
}
