// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Ipc.Interfaces;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.CLI;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Microsoft.ManifestGenerator;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Drop.WebApi;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tool.ServicePipDaemon;
using static BuildXL.Utilities.FormattableStringEx;
using static Tool.ServicePipDaemon.Statics;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Responsible for accepting and handling TCP/IP connections from clients.
    /// </summary>
    public sealed class DropDaemon : ServicePipDaemon.FinalizedByCreatorServicePipDaemon, IDisposable, IIpcOperationExecutor
    {
        private const int ServicePointParallelismForDrop = 200;

        /// <summary>
        /// Prefix for the error message of the exception that gets thrown when a symlink is attempted to be added to drop.
        /// </summary>
        internal const string SymlinkAddErrorMessagePrefix = "SymLinks may not be added to drop: ";

        private const string LogFileName = "DropDaemon";

        /// <nodoc/>
        public const string DropDLogPrefix = "(DropD) ";

        private static readonly int s_minIoThreadsForDrop = Environment.ProcessorCount * 10;

        private static readonly int s_minWorkerThreadsForDrop = Environment.ProcessorCount * 10;

        internal static readonly List<Option> DropConfigOptions = new List<Option>();

        internal static IEnumerable<Command> SupportedCommands => Commands.Values;

        /// <summary>
        /// Cached content of sealed directories.
        /// </summary>
        private readonly BuildXL.Utilities.Collections.ConcurrentBigMap<DirectoryArtifact, AsyncLazy<Possible<List<SealedDirectoryFile>>>> m_directoryArtifactContent = new();

        /// <summary>
        /// A mapping between a fully-qualified drop name and a corresponding dropConfig/VsoClient
        /// </summary>
        private readonly BuildXL.Utilities.Collections.ConcurrentBigMap<string, (DropConfig dropConfig, Lazy<Task<IDropClient>> lazyVsoClientTask)> m_vsoClients = new();

        #region Options and commands

        internal static readonly StrOption DropServiceConfigFile = RegisterDaemonConfigOption(new StrOption("dropServiceConfigFile")
        {
            ShortName = "c",
            HelpText = "Drop service configuration file",
            DefaultValue = null,
            Expander = (fileName) =>
            {
                var json = System.IO.File.ReadAllText(fileName);
                var jObject = JObject.Parse(json);
                return jObject.Properties().Select(prop => new ParsedOption(PrefixKind.Long, prop.Name, prop.Value.ToString()));
            },
        });

        internal static readonly StrOption DropNameOption = RegisterDropConfigOption(new StrOption("name")
        {
            ShortName = "n",
            HelpText = "Drop name",
            IsRequired = true,
        });

        internal static readonly UriOption DropEndpoint = RegisterDropConfigOption(new UriOption("service")
        {
            ShortName = "s",
            HelpText = "Drop endpoint URI",
            IsRequired = true,
        });

        internal static readonly IntOption BatchSize = RegisterDropConfigOption(new IntOption("batchSize")
        {
            ShortName = "bs",
            HelpText = "OBSOLETE due to the hardcoded config. (Size of batches in which to send 'associate' requests)",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultBatchSizeForAssociate,
        });

        internal static readonly IntOption MaxParallelUploads = RegisterDropConfigOption(new IntOption("maxParallelUploads")
        {
            ShortName = "mpu",
            HelpText = "Maximum number of uploads to issue to drop service in parallel",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultMaxParallelUploads,
        });

        internal static readonly IntOption NagleTimeMillis = RegisterDropConfigOption(new IntOption("nagleTimeMillis")
        {
            ShortName = "nt",
            HelpText = "OBSOLETE due to the hardcoded config. (Maximum time in milliseconds to wait before triggering a batch 'associate' request)",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultNagleTimeForAssociate.TotalMilliseconds,
        });

        internal static readonly IntOption RetentionDays = RegisterDropConfigOption(new IntOption("retentionDays")
        {
            ShortName = "rt",
            HelpText = "Drop retention time in days",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultRetention.TotalDays,
        });

        internal static readonly IntOption HttpSendTimeoutMillis = RegisterDropConfigOption(new IntOption("httpSendTimeoutMillis")
        {
            HelpText = "Timeout for http requests",
            IsRequired = false,
            DefaultValue = (int)DropConfig.DefaultHttpSendTimeout.TotalMilliseconds,
        });

        internal static readonly BoolOption EnableTelemetry = RegisterDropConfigOption(new BoolOption("enableTelemetry")
        {
            ShortName = "t",
            HelpText = "Verbose logging",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultEnableTelemetry,
        });

        internal static readonly BoolOption EnableChunkDedup = RegisterDropConfigOption(new BoolOption("enableChunkDedup")
        {
            ShortName = "cd",
            HelpText = "Chunk level dedup",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultEnableChunkDedup,
        });

        internal static readonly NullableIntOption OptionalDropDomainId = RegisterDropConfigOption(new NullableIntOption("domainId")
        {
            ShortName = "ddid",
            HelpText = "Optional drop domain id setting.",
            IsRequired = false,
            DefaultValue = null,
        });

        internal static readonly BoolOption GenerateBuildManifest = RegisterDropConfigOption(new BoolOption("generateBuildManifest")
        {
            ShortName = "gbm",
            HelpText = "Generate a Build Manifest",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultGenerateBuildManifest,
        });

        internal static readonly BoolOption SignBuildManifest = RegisterDropConfigOption(new BoolOption("signBuildManifest")
        {
            ShortName = "sbm",
            HelpText = "Sign the Build Manifest",
            IsRequired = false,
            DefaultValue = DropConfig.DefaultSignBuildManifest,
        });

        internal static readonly StrOption Repo = RegisterDropConfigOption(new StrOption("repo")
        {
            ShortName = "r",
            HelpText = "Repo location",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption Branch = RegisterDropConfigOption(new StrOption("branch")
        {
            ShortName = "b",
            HelpText = "Git branch name",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption CommitId = RegisterDropConfigOption(new StrOption("commitId")
        {
            ShortName = "ci",
            HelpText = "Git CommitId",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption CloudBuildId = RegisterDropConfigOption(new StrOption("cloudBuildId")
        {
            ShortName = "cbid",
            HelpText = "RelativeActivityId",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption BsiFileLocation = RegisterDropConfigOption(new StrOption("bsiFileLocation")
        {
            ShortName = "bsi",
            HelpText = "Represents the BuildSessionInfo: bsi.json file path.",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption MakeCatToolPath = RegisterDropConfigOption(new StrOption("makeCatToolPath")
        {
            ShortName = "makecat",
            HelpText = "Represents the Path to makecat.exe for Build Manifest Catalog generation.",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        internal static readonly StrOption EsrpManifestSignToolPath = RegisterDropConfigOption(new StrOption("esrpManifestSignToolPath")
        {
            ShortName = "esrpTool",
            HelpText = "Represents the Path to EsrpManifestSign.exe for Build Manifest Catalog Signing.",
            IsRequired = false,
            DefaultValue = string.Empty,
        });

        // ==============================================================================
        // 'addfile' and 'addartifacts' parameters
        // ==============================================================================
        internal static readonly StrOption RelativeDropPath = new StrOption("dropPath")
        {
            ShortName = "d",
            HelpText = "Relative drop path",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption RelativeDirectoryDropPath = new StrOption("directoryDropPath")
        {
            ShortName = "dird",
            HelpText = "Relative drop path for directory",
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryContentFilter = new StrOption("directoryFilter")
        {
            ShortName = "dcfilter",
            HelpText = "Directory content filter (only files that match the filter will be added to drop).",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly StrOption DirectoryRelativePathReplace = new StrOption("directoryRelativePathReplace")
        {
            ShortName = "drpr",
            HelpText = "Relative path replace arguments.",
            DefaultValue = null,
            IsRequired = false,
            IsMultiValue = true,
        };

        internal static readonly Command StartNoDropCmd = RegisterCommand(
            name: "start-nodrop",
            description: @"Starts a server process without a backing VSO drop client (useful for testing/pinging the daemon).",
            needsIpcClient: false,
            clientAction: (conf, _) =>
            {
                var daemonConfig = CreateDaemonConfig(conf);
                using (var daemon = new DropDaemon(conf.Config.Parser, daemonConfig))
                {
                    daemon.Start();
                    daemon.Completion.GetAwaiter().GetResult();
                    return 0;
                }
            });

        internal static readonly Command StartCmd = RegisterCommand(
           name: "start",
           description: "Starts the server process.",
           options: DropConfigOptions.Union(new[] { IpcServerMonikerOptional }),
           needsIpcClient: false,
           clientAction: (conf, _) =>
           {
               SetupThreadPoolAndServicePoint(s_minWorkerThreadsForDrop, s_minIoThreadsForDrop, ServicePointParallelismForDrop);
               var daemonConf = CreateDaemonConfig(conf);

               if (daemonConf.MaxConcurrentClients <= 1)
               {
                   conf.Logger.Error($"Must specify at least 2 '{nameof(DaemonConfig.MaxConcurrentClients)}' when running DropDaemon to avoid deadlock when stopping this daemon from a different client");
                   return -1;
               }

               using (var client = CreateClient(conf.Get(IpcServerMonikerOptional), daemonConf))
               using (var daemon = new DropDaemon(
                   parser: conf.Config.Parser,
                   daemonConfig: daemonConf,
                   client: client))
               {
                   daemon.Start();
                   daemon.Completion.GetAwaiter().GetResult();
                   return 0;
               }
           });

        internal static readonly Command StartDaemonCmd = RegisterCommand(
           name: "start-daemon",
           description: "Starts the server process in background (as daemon).",
           options: DropConfigOptions,
           needsIpcClient: false,
           clientAction: (conf, _) =>
           {
               using (var daemon = new Process())
               {
                   bool shellExecute = conf.Get(ShellExecute);
                   daemon.StartInfo.FileName = AssemblyHelper.GetAssemblyLocation(System.Reflection.Assembly.GetEntryAssembly());
                   daemon.StartInfo.Arguments = "start " + conf.Config.Render();
                   daemon.StartInfo.LoadUserProfile = false;
                   daemon.StartInfo.UseShellExecute = shellExecute;
                   daemon.StartInfo.CreateNoWindow = !shellExecute;
                   daemon.Start();
               }

               return 0;
           });

        internal static readonly Command CreateDropCmd = RegisterCommand(
           name: "create",
           description: "[RPC] Invokes the 'create' operation.",
           options: DropConfigOptions,
           clientAction: SyncRPCSend,
           serverAction: async (conf, dropDaemon) =>
           {
               var dropConfig = CreateDropConfig(conf);
               var daemon = dropDaemon as DropDaemon;
               var name = FullyQualifiedDropName(dropConfig);
               daemon.Logger.Info($"[CREATE]: Started at '{name}'");
               if (dropConfig.SignBuildManifest && !dropConfig.GenerateBuildManifest)
               {
                   conf.Logger.Warning("SignBuildManifest = true and GenerateBuildManifest = false. The BuildManifest will not be generated, and thus cannot be signed.");
               }

               if (!BuildManifestHelper.VerifyBuildManifestRequirements(dropConfig, out string errMessage))
               {
                   daemon.Logger.Error($"[CREATE]: Cannot create drop due to an invalid build manifest configuration: {errMessage}");
                   return new IpcResult(IpcResultStatus.InvalidInput, errMessage);
               }

               daemon.EnsureVsoClientIsCreated(dropConfig);
               IIpcResult result = await daemon.CreateAsync(name);
               daemon.Logger.Info($"[CREATE]: {result}");
               return result;
           });

        internal static readonly Command FinalizeCmd = RegisterCommand(
            name: "finalize",
            description: "[RPC] Invokes the 'finalize' operation for all drops",
            clientAction: SyncRPCSend,
            serverAction: async (conf, dropDaemon) =>
            {
                var daemon = dropDaemon as DropDaemon;
                daemon.Logger.Info("[FINALIZE] Started finalizing all running drops.");

                // Build manifest logic is not a part of the FinalizeAsync/DoFinalize because daemon-wide finalization can be either triggered
                // by a finalize call from BuildXL or by the logic in FinalizedByCreatorServicePipDaemon. We do not want to create manifests if
                // some of the upstream drop operations failed or did not run at all (manifests created in such cases might not represent drops
                // that a build was expected to produce).
                // If we receive a finalize command, we are guaranteed that all upstream drop operations were successful (or in case of finalizeDrop,
                // all operations for a particular drop were successful).
                // Note: if there is a finalizeDrop call for drop_A, that call is successfully executed, and an operation for another drop (drop_B)
                // fails, both drops (drop_A and drop_B) will be finalized, but only drop_A will contain build manifest.
                var buildManifestResult = await daemon.ProcessBuildManifestsAsync();
                if (!buildManifestResult.Succeeded)
                {
                    // drop-specific error is already logged
                    daemon.Logger.Info($"[FINALIZE] Operation failed while processing a build manifest.");
                    return buildManifestResult;
                }

                IIpcResult result = await daemon.FinalizeAsync();
                daemon.Logger.Info($"[FINALIZE] {result}");
                return result;
            });

        internal static readonly Command FinalizeDropCmd = RegisterCommand(
           name: "finalizeDrop",
           description: "[RPC] Invokes the 'finalize' operation for a particular drop",
           options: DropConfigOptions,
           clientAction: SyncRPCSend,
           serverAction: async (conf, dropDaemon) =>
           {
               var daemon = dropDaemon as DropDaemon;
               var dropConfig = CreateDropConfig(conf);
               daemon.Logger.Info($"[FINALIZE] Started finalizing '{dropConfig.Name}'.");

               var buildManifestResult = await daemon.ProcessBuildManifestForDropAsync(dropConfig);
               if (!buildManifestResult.Succeeded)
               {
                   daemon.Logger.Info($"[FINALIZE] Operation failed while processing a build manifest.");
                   return buildManifestResult;
               }

               IIpcResult result = await daemon.FinalizeSingleDropAsync(dropConfig);
               daemon.Logger.Info($"[FINALIZE] {result}");
               return result;
           });

        internal static readonly Command FinalizeDropAndStopDaemonCmd = RegisterCommand(
            name: "finalize-and-stop",
            description: "[RPC] Invokes the 'finalize' operation; then stops the daemon.",
            clientAction: SyncRPCSend,
            serverAction: Command.Compose(FinalizeCmd.ServerAction, StopDaemonCmd.ServerAction));

        internal static readonly Command AddFileToDropCmd = RegisterCommand(
            name: "addfile",
            description: "[RPC] invokes the 'addfile' operation.",
            options: DropConfigOptions.Union(new Option[] { File, RelativeDropPath, HashOptional }),
            clientAction: SyncRPCSend,
            serverAction: async (conf, dropDaemon) =>
            {
                var daemon = dropDaemon as DropDaemon;
                daemon.Logger.Verbose("[ADDFILE] Started");
                var dropConfig = CreateDropConfig(conf);
                string filePath = conf.Get(File);
                string hashValue = conf.Get(HashOptional);
                var contentInfo = string.IsNullOrEmpty(hashValue) ? null : (FileContentInfo?)FileContentInfo.Parse(hashValue);
                var dropItem = new DropItemForFile(DropDaemon.FullyQualifiedDropName(dropConfig), filePath, conf.Get(RelativeDropPath), contentInfo);
                IIpcResult result = System.IO.File.Exists(filePath)
                    ? await daemon.AddFileAsync(dropItem)
                    : new IpcResult(IpcResultStatus.ExecutionError, "file '" + filePath + "' does not exist");
                daemon.Logger.Verbose("[ADDFILE] " + result);
                return result;
            });

        internal static readonly Command AddArtifactsToDropCmd = RegisterCommand(
            name: "addartifacts",
            description: "[RPC] invokes the 'addartifacts' operation.",
            options: DropConfigOptions.Union(new Option[] { IpcServerMonikerRequired, File, FileId, HashOptional, RelativeDropPath, Directory, DirectoryId, RelativeDirectoryDropPath, DirectoryContentFilter, DirectoryRelativePathReplace }),
            clientAction: SyncRPCSend,
            serverAction: async (conf, dropDaemon) =>
            {
                var daemon = dropDaemon as DropDaemon;
                daemon.Logger.Verbose("[ADDARTIFACTS] Started");
                var dropConfig = CreateDropConfig(conf);
                daemon.EnsureVsoClientIsCreated(dropConfig);

                var result = await AddArtifactsToDropInternalAsync(conf, daemon);

                daemon.Logger.Verbose("[ADDARTIFACTS] " + result);
                return result;
            });

        #endregion

        /// <summary>
        /// The purpose of this ctor is to force 'predictable' initialization of static fields.
        /// </summary>
        static DropDaemon()
        {
            // noop
        }

        /// <nodoc />
        public DropDaemon(IParser parser, DaemonConfig daemonConfig, IIpcProvider rpcProvider = null, Client client = null)
            : base(parser,
                   daemonConfig,
                   !string.IsNullOrWhiteSpace(daemonConfig?.LogDir) ? new FileLogger(daemonConfig.LogDir, LogFileName, daemonConfig.Moniker, daemonConfig.Verbose, DropDLogPrefix) : daemonConfig.Logger,
                   rpcProvider,
                   client)
        {
        }

        internal static void EnsureCommandsInitialized()
        {
            Contract.Assert(Commands != null);

            // these operations are quite expensive, however, we expect to call this method only once per drop, so it should cause any perf downgrade
            var numCommandsBase = typeof(ServicePipDaemon.ServicePipDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();
            var numCommandsDropD = typeof(DropDaemon).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Command)).Count();

            if (Commands.Count != numCommandsBase + numCommandsDropD)
            {
                Contract.Assert(false, $"The list of commands was not properly initialized (# of initialized commands = {Commands.Count}; # of ServicePipDaemon commands = {numCommandsBase}; # of DropDaemon commands = {numCommandsDropD})");
            }
        }

        /// <summary>
        /// Creates the drop.  Handles drop-related exceptions by omitting their stack traces.
        /// In all cases emits an appropriate <see cref="DropCreationEvent"/> indicating the
        /// result of this operation.
        /// </summary>
        protected override async Task<IIpcResult> DoCreateAsync(string name)
        {
            if (name == null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, "Name cannot be null when creating a drop.");
            }

            if (!m_vsoClients.TryGetValue(name, out var configAndClient))
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"Could not find VsoClient for a provided drop name: '{name}'");
            }

            DropCreationEvent dropCreationEvent =
                await SendDropEtwEventAsync(
                    WrapDropErrorsIntoDropEtwEvent(() => InternalCreateAsync(configAndClient.lazyVsoClientTask.Value)),
                    configAndClient.lazyVsoClientTask.Value);

            return dropCreationEvent.Succeeded
                ? IpcResult.Success(I($"Drop '{configAndClient.dropConfig.Name}' created."))
                : new IpcResult(ParseIpcStatus(dropCreationEvent.AdditionalInformation), dropCreationEvent.ErrorMessage);
        }

        /// <summary>
        ///     Invokes the 'drop addfile' operation by delegating to <see cref="IDropClient.AddFileAsync"/>.
        ///     Handles drop-related exceptions by omitting their stack traces.
        /// </summary>
        public Task<IIpcResult> AddFileAsync(IDropItem dropItem)
        {
            return AddFileAsync(dropItem, IsSymLinkOrMountPoint);
        }

        internal async Task<IIpcResult> AddFileAsync(IDropItem dropItem, Func<string, bool> symlinkTester)
        {
            Contract.Requires(dropItem != null);

            // Check if the file is a symlink, only if the file exists on disk at this point; if it is a symlink, reject it outright.
            if (System.IO.File.Exists(dropItem.FullFilePath) && symlinkTester(dropItem.FullFilePath))
            {
                return new IpcResult(IpcResultStatus.ExecutionError, SymlinkAddErrorMessagePrefix + dropItem.FullFilePath);
            }

            if (!m_vsoClients.TryGetValue(dropItem.FullyQualifiedDropName, out var configAndClient))
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"Could not find VsoClient for a provided drop name: '{dropItem.FullyQualifiedDropName}' (file '{dropItem.FullFilePath}')");
            }

            return await WrapDropErrorsIntoIpcResult(async () =>
            {
                IDropClient dropClient = await configAndClient.lazyVsoClientTask.Value;
                AddFileResult result = await dropClient.AddFileAsync(dropItem);

                switch (result)
                {
                    case AddFileResult.Associated:
                    case AddFileResult.UploadedAndAssociated:
                    case AddFileResult.SkippedAsDuplicate:
                        return IpcResult.Success(I($"File '{dropItem.FullFilePath}' {result} under '{dropItem.RelativeDropPath}' in drop '{configAndClient.dropConfig.Name}'."));
                    case AddFileResult.RegisterFileForBuildManifestFailure:
                        return new IpcResult(IpcResultStatus.ExecutionError, $"Failure during BuildManifest Hash generation for File '{dropItem.FullFilePath}' {result} under '{dropItem.RelativeDropPath}' in drop '{configAndClient.dropConfig.Name}'.");
                    default:
                        return new IpcResult(IpcResultStatus.ExecutionError, $"Unhandled drop result: {result}");
                }
            });
        }

        private async Task<IIpcResult> ProcessBuildManifestsAsync()
        {
            var tasks = m_vsoClients.Values.Select(async client =>
                {
                    await Task.Yield();
                    var dropClient = await client.lazyVsoClientTask.Value;
                    // return early if we have already finalized this drop
                    if (dropClient.AttemptedFinalization)
                    {
                        return IpcResult.Success();
                    }

                    return await ProcessBuildManifestForDropAsync(client.dropConfig);
                }).ToArray();

            var ipcResults = await TaskUtilities.SafeWhenAll(tasks);
            return IpcResult.Merge(ipcResults);
        }

        private async Task<IIpcResult> ProcessBuildManifestForDropAsync(DropConfig dropConfig)
        {
            if (!dropConfig.GenerateBuildManifest)
            {
                return IpcResult.Success();
            }

            var bsiResult = await UploadBsiFileAsync(dropConfig);
            if (!bsiResult.Succeeded)
            {
                Logger.Error($"[FINALIZE ({dropConfig.Name})] Failure occurred during BuildSessionInfo (bsi) upload: {bsiResult.Payload}");
                return bsiResult;
            }

            var buildManifestResult = await GenerateAndUploadBuildManifestFileWithSignedCatalogAsync(dropConfig);
            if (!buildManifestResult.Succeeded)
            {
                Logger.Error($"[FINALIZE ({dropConfig.Name})] Failure occurred during Build Manifest upload: {buildManifestResult.Payload}");
                return buildManifestResult;
            }

            return IpcResult.Success();
        }

        /// <summary>
        /// Uploads the bsi.json for the given drop.
        /// Should be called only when DropConfig.GenerateBuildManifest is true.
        /// </summary>
        private async Task<IIpcResult> UploadBsiFileAsync(DropConfig dropConfig)
        {
            Contract.Requires(dropConfig.GenerateBuildManifest, "GenerateBuildManifestData API called even though Build Manifest Generation is Disabled in DropConfig");

            if (!System.IO.File.Exists(dropConfig.BsiFileLocation))
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"BuildSessionInfo not found at provided BsiFileLocation: '{dropConfig.BsiFileLocation}'");
            }

            var bsiDropItem = new DropItemForFile(FullyQualifiedDropName(dropConfig), dropConfig.BsiFileLocation, relativeDropPath: BuildManifestHelper.DropBsiPath);
            return await AddFileAsync(bsiDropItem);
        }

        /// <summary>
        /// Generates and uploads the Manifest.json on the master using all file hashes computed and stored 
        /// by workers using <see cref="VsoClient.RegisterFilesForBuildManifestAsync"/> for the given drop.
        /// Should be called only when DropConfig.GenerateBuildManifest is true.
        /// </summary>
        private async Task<IIpcResult> GenerateAndUploadBuildManifestFileWithSignedCatalogAsync(DropConfig dropConfig)
        {
            Contract.Requires(dropConfig.GenerateBuildManifest, "GenerateBuildManifestData API called even though Build Manifest Generation is Disabled in DropConfig");

            var bxlResult = await ApiClient.GenerateBuildManifestFileList(FullyQualifiedDropName(dropConfig));

            if (!bxlResult.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"GenerateBuildManifestData API call failed for Drop: {dropConfig.Name}. Failure: {bxlResult.Failure.DescribeIncludingInnerFailures()}");
            }

            List<BuildManifestFile> manifestFileListForDrop = bxlResult.Result
                .Select(fileInfo => new BuildManifestFile(fileInfo.RelativePath, fileInfo.AzureArtifactsHash, fileInfo.BuildManifestHash))
                .ToList();

            BuildManifestData buildManifestData = new BuildManifestData(
                CloudBuildManifestV1.ManifestInfoV1.Version,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                dropConfig.CloudBuildId,
                dropConfig.Repo,
                dropConfig.Branch,
                dropConfig.CommitId,
                manifestFileListForDrop);

            string localFilePath;
            string buildManifestJsonStr = BuildManifestData.GenerateBuildManifestJsonString(buildManifestData);

            try
            {
                localFilePath = Path.GetTempFileName();
                System.IO.File.WriteAllText(localFilePath, buildManifestJsonStr);
            }
            catch (Exception ex)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"Exception while trying to store Build Manifest locally before drop upload: {ex}");
            }

            var dropItem = new DropItemForFile(FullyQualifiedDropName(dropConfig), localFilePath, relativeDropPath: BuildManifestHelper.DropBuildManifestPath);
            var buildManifestUploadResult = await AddFileAsync(dropItem);

            if (!buildManifestUploadResult.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"Failure occurred during Build Manifest upload: {buildManifestUploadResult.Payload}");
            }

            if (!dropConfig.SignBuildManifest)
            {
                return IpcResult.Success("Unsigned Build Manifest generated and uploaded successfully");
            }

            var startTime = DateTime.UtcNow;
            var signManifestResult = await GenerateAndSignBuildManifestCatalogFileAsync(dropConfig, localFilePath);
            long signTimeMs = (long)DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
            Logger.Info($"Build Manifest signing via EsrpManifestSign completed in {signTimeMs} ms. Succeeded: {signManifestResult.Succeeded}");

            return signManifestResult;
        }

        /// <summary>
        /// Generates and uploads a catalog file for <see cref="BuildManifestHelper.BuildManifestFilename"/> and <see cref="BuildManifestHelper.BsiFilename"/>
        /// Should be called only when DropConfig.GenerateBuildManifest is true and DropConfig.SignBuildManifest is true.
        /// </summary>
        private async Task<IIpcResult> GenerateAndSignBuildManifestCatalogFileAsync(DropConfig dropConfig, string buildManifestLocalPath)
        {
            Contract.Requires(dropConfig.GenerateBuildManifest, "GenerateAndSignBuildManifestCatalogFileAsync API called even though Build Manifest Generation is Disabled in DropConfig");
            Contract.Requires(dropConfig.SignBuildManifest, "GenerateAndSignBuildManifestCatalogFileAsync API called even though SignBuildManifest is Disabled in DropConfig");

            var generateCatalogResult = await BuildManifestHelper.GenerateSignedCatalogAsync(
                dropConfig.MakeCatToolPath,
                dropConfig.EsrpManifestSignToolPath,
                buildManifestLocalPath,
                dropConfig.BsiFileLocation);

            if (!generateCatalogResult.Success)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, generateCatalogResult.Payload);
            }

            string catPath = generateCatalogResult.Payload;

            var dropItem = new DropItemForFile(FullyQualifiedDropName(dropConfig), catPath, relativeDropPath: BuildManifestHelper.DropCatalogFilePath);
            var uploadCatFileResult = await AddFileAsync(dropItem);

            // Delete temporary file created during Build Manifest signing
            try
            {
                System.IO.File.Delete(catPath);
            }
            catch (IOException)
            {
                // Can be ignored
            }

            if (!uploadCatFileResult.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, $"Failure occurred during Build Manifest CAT file upload: {uploadCatFileResult.Payload}");
            }

            return IpcResult.Success("Catalog file signed and uploaded successfully");
        }

        /// <summary>
        /// Finalizes the drop.  Handles drop-related exceptions by omitting their stack traces.
        /// In all cases emits an appropriate <see cref="DropFinalizationEvent"/> indicating the
        /// result of this operation.
        /// </summary>
        protected override async Task<IIpcResult> DoFinalizeAsync()
        {
            var finalizationTasks = m_vsoClients.Values.Select(async client =>
               {
                   var dropClient = await client.lazyVsoClientTask.Value;
                   if (dropClient.AttemptedFinalization)
                   {
                       return IpcResult.Success(I($"An attempt to finalize drop {client.dropConfig.Name} has already been made; skipping this finalization."));
                   }

                   return await FinalizeSingleDropAsync(client.dropConfig, client.lazyVsoClientTask.Value);
               }).ToArray();

            var results = await TaskUtilities.SafeWhenAll(finalizationTasks);
            return IpcResult.Merge(results);
        }

        private async Task<IIpcResult> FinalizeSingleDropAsync(DropConfig dropConfig, Task<IDropClient> dropClientTask = null)
        {
            await Task.Yield();
            if (dropClientTask == null)
            {
                var dropName = FullyQualifiedDropName(dropConfig);
                if (!m_vsoClients.TryGetValue(dropName, out var configAndClient))
                {
                    return new IpcResult(IpcResultStatus.ExecutionError, $"Could not find VsoClient for a provided drop name: '{dropName}'");
                }

                dropClientTask = configAndClient.lazyVsoClientTask.Value;
            }

            // We invoke 'finalize' regardless whether the drop is finalize (dropClient.IsFinalized) or not.
            var dropFinalizationEvent =
                await SendDropEtwEventAsync(
                    WrapDropErrorsIntoDropEtwEvent(() => InternalFinalizeAsync(dropClientTask)),
                    dropClientTask);

            return dropFinalizationEvent.Succeeded
                ? IpcResult.Success(I($"Drop {dropConfig.Name} finalized"))
                : new IpcResult(ParseIpcStatus(dropFinalizationEvent.AdditionalInformation), dropFinalizationEvent.ErrorMessage);
        }

        /// <nodoc />
        public override void Dispose()
        {
            ReportStatisticsAsync().GetAwaiter().GetResult();

            foreach (var kvp in m_vsoClients)
            {
                kvp.Value.lazyVsoClientTask.Value.Result.Dispose();
            }

            base.Dispose();
        }

        /// <summary>
        /// Invokes the 'drop create' operation by delegating to <see cref="IDropClient.CreateAsync"/>.
        ///
        /// If successful, returns <see cref="DropCreationEvent"/> with <see cref="DropOperationBaseEvent.Succeeded"/>
        /// set to true, <see cref="DropCreationEvent.DropExpirationInDays"/> set to drop expiration in days,
        /// and <see cref="DropOperationBaseEvent.AdditionalInformation"/> set to the textual representation
        /// of the returned <see cref="DropItem"/> object.
        ///
        /// Doesn't handle any exceptions.
        /// </summary>
        private async Task<DropCreationEvent> InternalCreateAsync(Task<IDropClient> vsoClientTask)
        {
            IDropClient dropClient = await vsoClientTask;
            DropItem dropItem = await dropClient.CreateAsync();
            return new DropCreationEvent()
            {
                Succeeded = true,
                AdditionalInformation = DropItemToString(dropItem),
                DropExpirationInDays = ComputeDropItemExpiration(dropItem),
            };
        }

        /// <summary>
        /// Invokes the 'drop finalize' operation by delegating to <see cref="IDropClient.FinalizeAsync"/>.
        ///
        /// If successful, returns <see cref="DropFinalizationEvent"/> with <see cref="DropOperationBaseEvent.Succeeded"/>
        /// set to true.
        ///
        /// Doesn't handle any exceptions.
        /// </summary>
        private async Task<DropFinalizationEvent> InternalFinalizeAsync(Task<IDropClient> dropClientTask)
        {
            var dropClient = await dropClientTask;
            await dropClient.FinalizeAsync();
            return new DropFinalizationEvent()
            {
                Succeeded = true,
            };
        }

        private async Task ReportStatisticsAsync()
        {
            var stats = new Dictionary<string, long>();
            foreach (var kvp in m_vsoClients)
            {
                if (kvp.Value.lazyVsoClientTask.Value.IsCompleted && !kvp.Value.lazyVsoClientTask.Value.IsFaulted)
                {
                    m_logger.Warning($"No stats collected for drop '{kvp.Value.dropConfig.Name}' due to an error.");
                    continue;
                }

                var vsoClient = await kvp.Value.lazyVsoClientTask.Value;
                var clientStats = vsoClient.GetStats();
                if (clientStats == null || clientStats.Count == 0)
                {
                    m_logger.Info("No stats recorded by drop client of type " + vsoClient.GetType().Name);
                    continue;
                }

                foreach (var statistic in clientStats)
                {
                    if (!stats.ContainsKey(statistic.Key))
                    {
                        stats.Add(statistic.Key, 0);
                    }

                    stats[statistic.Key] += statistic.Value;
                }
            }

            if (stats != null && stats.Any())
            {
                // log stats
                m_logger.Info("Statistics: ");
                m_logger.Info(string.Join(Environment.NewLine, stats.Select(s => s.Key + " = " + s.Value)));

                stats.AddRange(m_counters.AsStatistics());

                // report stats to BuildXL (if m_client is specified)
                if (ApiClient != null)
                {
                    var possiblyReported = await ApiClient.ReportStatistics(stats);
                    if (possiblyReported.Succeeded && possiblyReported.Result)
                    {
                        m_logger.Info("Statistics successfully reported to BuildXL.");
                    }
                    else
                    {
                        var errorDescription = possiblyReported.Succeeded ? string.Empty : possiblyReported.Failure.Describe();
                        m_logger.Warning("Reporting stats to BuildXL failed. " + errorDescription);
                    }
                }
            }
        }

        private delegate TResult ErrorFactory<TResult>(string message, IpcResultStatus status);

        private static Task<IIpcResult> WrapDropErrorsIntoIpcResult(Func<Task<IIpcResult>> factory)
        {
            return HandleKnownErrorsAsync(
                factory,
                (errorMessage, status) => new IpcResult(status, errorMessage));
        }

        private static Task<TDropEvent> WrapDropErrorsIntoDropEtwEvent<TDropEvent>(Func<Task<TDropEvent>> factory) where TDropEvent : DropOperationBaseEvent
        {
            return HandleKnownErrorsAsync(
                factory,
                (errorMessage, errorKind) =>
                {
                    var dropEvent = Activator.CreateInstance<TDropEvent>();
                    dropEvent.Succeeded = false;
                    dropEvent.ErrorMessage = errorMessage;
                    dropEvent.AdditionalInformation = RenderIpcStatus(errorKind);
                    return dropEvent;
                });
        }

        private static string RenderIpcStatus(IpcResultStatus status)
        {
            return status.ToString();
        }

        private static IpcResultStatus ParseIpcStatus(string statusString, IpcResultStatus defaultValue = IpcResultStatus.ExecutionError)
        {
            return Enum.TryParse<IpcResultStatus>(statusString, out var value)
                ? value
                : defaultValue;
        }

        /// <summary>
        /// BuildXL's classification of different <see cref="IpcResultStatus"/> values:
        ///   - <see cref="IpcResultStatus.InvalidInput"/>      --> <see cref="Keywords.UserError"/>
        ///   - <see cref="IpcResultStatus.TransmissionError"/> --> <see cref="Keywords.InfrastructureError"/>
        ///   - all other errors                                --> InternalError
        /// </summary>
        private static async Task<TResult> HandleKnownErrorsAsync<TResult>(Func<Task<TResult>> factory, ErrorFactory<TResult> errorValueFactory)
        {
            try
            {
                return await factory();
            }
            catch (VssUnauthorizedException e)
            {
                return errorValueFactory("[DROP AUTH ERROR] " + e.Message, IpcResultStatus.InvalidInput);
            }
            catch (VssResourceNotFoundException e)
            {
                return errorValueFactory("[DROP SERVICE ERROR] " + e.Message, IpcResultStatus.TransmissionError);
            }
            catch (DropServiceException e)
            {
                var status = e.Message.Contains("already exists")
                    ? IpcResultStatus.InvalidInput
                    : IpcResultStatus.TransmissionError;
                return errorValueFactory("[DROP SERVICE ERROR] " + e.Message, status);
            }
            catch (DaemonException e)
            {
                return errorValueFactory("[DAEMON ERROR] " + e.Message, IpcResultStatus.ExecutionError);
            }
        }

        private static string DropItemToString(DropItem dropItem)
        {
            try
            {
                return dropItem?.ToJson().ToString();
            }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
            catch
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private static int ComputeDropItemExpiration(DropItem dropItem)
        {
            DateTime? expirationDate;
            return dropItem.TryGetExpirationTime(out expirationDate) || expirationDate.HasValue
                ? (int)expirationDate.Value.Subtract(DateTime.UtcNow).TotalDays
                : -1;
        }

        private async Task<T> SendDropEtwEventAsync<T>(Task<T> task, Task<IDropClient> dropClient) where T : DropOperationBaseEvent
        {
            long startTime = DateTime.UtcNow.Ticks;
            T dropEvent = null;
            try
            {
                dropEvent = await task;
                return dropEvent;
            }
            finally
            {
                // if 'task' failed, create an event indicating an error
                if (dropEvent == null)
                {
                    dropEvent = Activator.CreateInstance<T>();
                    dropEvent.Succeeded = false;
                    dropEvent.ErrorMessage = "internal error";
                }

                // common properties: execution time, drop type, drop url
                dropEvent.ElapsedTimeTicks = DateTime.UtcNow.Ticks - startTime;
                dropEvent.DropType = "VsoDrop";
                dropEvent.DropUrl = (await dropClient).DropUrl;

                // send event
                m_etwLogger.Log(dropEvent);
            }
        }

        internal static DropConfig CreateDropConfig(ConfiguredCommand conf)
        {
            byte? domainId;
            checked
            {
                domainId = (byte?)conf.Get(OptionalDropDomainId);
            }

            return new DropConfig(
                dropName: conf.Get(DropNameOption),
                serviceEndpoint: conf.Get(DropEndpoint),
                maxParallelUploads: conf.Get(MaxParallelUploads),
                retention: TimeSpan.FromDays(conf.Get(RetentionDays)),
                httpSendTimeout: TimeSpan.FromMilliseconds(conf.Get(HttpSendTimeoutMillis)),
                enableTelemetry: conf.Get(EnableTelemetry),
                enableChunkDedup: conf.Get(EnableChunkDedup),
                artifactLogName: conf.Get(ArtifactLogName),
                batchSize: conf.Get(BatchSize),
                dropDomainId: domainId,
                generateBuildManifest: conf.Get(GenerateBuildManifest),
                signBuildManifest: conf.Get(SignBuildManifest),
                repo: conf.Get(Repo),
                branch: conf.Get(Branch),
                commitId: conf.Get(CommitId),
                cloudBuildId: conf.Get(CloudBuildId),
                bsiFileLocation: conf.Get(BsiFileLocation),
                makeCatToolPath: conf.Get(MakeCatToolPath),
                esrpManifestSignToolPath: conf.Get(EsrpManifestSignToolPath));
        }

        private static T RegisterDropConfigOption<T>(T option) where T : Option => RegisterOption(DropConfigOptions, option);

        private static Client CreateClient(string serverMoniker, IClientConfig config)
        {
            return serverMoniker != null
                ? Client.Create(serverMoniker, config)
                : null;
        }

        private static async Task<IIpcResult> AddArtifactsToDropInternalAsync(ConfiguredCommand conf, DropDaemon daemon)
        {
            var dropName = conf.Get(DropNameOption);
            var serviceEndpoint = conf.Get(DropEndpoint);
            var fullDropName = FullyQualifiedDropName(serviceEndpoint, dropName);

            var files = File.GetValues(conf.Config).ToArray();
            var fileIds = FileId.GetValues(conf.Config).ToArray();
            var hashes = HashOptional.GetValues(conf.Config).ToArray();
            var dropPaths = RelativeDropPath.GetValues(conf.Config).ToArray();

            if (files.Length != fileIds.Length || files.Length != hashes.Length || files.Length != dropPaths.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"File counts don't match: #files = {files.Length}, #fileIds = {fileIds.Length}, #hashes = {hashes.Length}, #dropPaths = {dropPaths.Length}"));
            }

            var directoryPaths = Directory.GetValues(conf.Config).ToArray();
            var directoryIds = DirectoryId.GetValues(conf.Config).ToArray();
            var directoryDropPaths = RelativeDirectoryDropPath.GetValues(conf.Config).ToArray();
            var directoryFilters = DirectoryContentFilter.GetValues(conf.Config).ToArray();
            var directoryRelativePathsReplaceSerialized = DirectoryRelativePathReplace.GetValues(conf.Config).ToArray();

            if (directoryPaths.Length != directoryIds.Length || directoryPaths.Length != directoryDropPaths.Length || directoryPaths.Length != directoryFilters.Length || directoryPaths.Length != directoryRelativePathsReplaceSerialized.Length)
            {
                return new IpcResult(
                    IpcResultStatus.GenericError,
                    I($"Directory counts don't match: #directories = {directoryPaths.Length}, #directoryIds = {directoryIds.Length}, #dropPaths = {directoryDropPaths.Length}, #directoryFilters = {directoryFilters.Length}, #directoryRelativePathReplace = {directoryRelativePathsReplaceSerialized.Length}"));
            }

            var possibleFilters = InitializeFilters(directoryFilters);
            if (!possibleFilters.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleFilters.Failure.Describe());
            }

            var possibleRelativePathReplacementArguments = InitializeRelativePathReplacementArguments(directoryRelativePathsReplaceSerialized);
            if (!possibleRelativePathReplacementArguments.Succeeded)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, possibleRelativePathReplacementArguments.Failure.Describe());
            }

            var dropFileItemsKeyedByIsAbsent = Enumerable
                .Range(0, files.Length)
                .Select(i => new DropItemForBuildXLFile(
                    daemon.ApiClient,
                    fullDropName,
                    filePath: files[i],
                    fileId: fileIds[i],
                    fileContentInfo: FileContentInfo.Parse(hashes[i]),
                    relativeDropPath: dropPaths[i]))
                .ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.Hash));

            // If a user specified a particular file to be added to drop, this file must be a part of drop.
            // The missing files will not get into the drop, so we emit an error.
            if (dropFileItemsKeyedByIsAbsent[true].Any())
            {
                string missingFiles = string.Join(Environment.NewLine, dropFileItemsKeyedByIsAbsent[true].Select(f => $"{f.FullFilePath} ({f})"));
                return new IpcResult(
                    IpcResultStatus.InvalidInput,
                    I($"Cannot add the following files to drop because they do not exist:{Environment.NewLine}{missingFiles}"));
            }

            (IEnumerable<DropItemForBuildXLFile> dropDirectoryMemberItems, string error) = await CreateDropItemsForDirectoriesAsync(
                daemon,
                fullDropName,
                directoryPaths,
                directoryIds,
                directoryDropPaths,
                possibleFilters.Result,
                possibleRelativePathReplacementArguments.Result);

            if (error != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, error);
            }

            var groupedDirectoriesContent = dropDirectoryMemberItems.ToLookup(f => WellKnownContentHashUtilities.IsAbsentFileHash(f.Hash));

            // we allow missing files inside of directories only if those files are output files (e.g., optional or temporary files) 
            if (groupedDirectoriesContent[true].Any(f => !f.IsOutputFile))
            {
                return new IpcResult(
                    IpcResultStatus.InvalidInput,
                    I($"Uploading missing source file(s) is not supported:{Environment.NewLine}{string.Join(Environment.NewLine, groupedDirectoriesContent[true].Where(f => !f.IsOutputFile))}"));
            }

            // return early if there is nothing to upload
            if (!dropFileItemsKeyedByIsAbsent[false].Any() && !groupedDirectoriesContent[false].Any())
            {
                return new IpcResult(IpcResultStatus.Success, string.Empty);
            }

            return await AddDropItemsAsync(daemon, dropFileItemsKeyedByIsAbsent[false].Concat(groupedDirectoriesContent[false]));
        }

        private static Possible<RelativePathReplacementArguments[]> InitializeRelativePathReplacementArguments(string[] serializedValues)
        {
            const char DelimChar = '#';
            const string NoRereplacement = "##";

            /*
                Format:
                    Replacement arguments are not specified: "##"
                    Replacement arguments are specified:     "#{searchString}#{replaceString}#"
             */

            var initializedValues = new RelativePathReplacementArguments[serializedValues.Length];
            for (int i = 0; i < serializedValues.Length; i++)
            {
                if (serializedValues[i] == NoRereplacement)
                {
                    initializedValues[i] = RelativePathReplacementArguments.Invalid;
                    continue;
                }

                var arr = serializedValues[i].Split(DelimChar);
                if (arr.Length != 4
                    || arr[0].Length != 0
                    || arr[3].Length != 0)
                {
                    return new Failure<string>($"Failed to deserialize relative path replacement arguments: '{serializedValues[i]}'.");
                }

                initializedValues[i] = new RelativePathReplacementArguments(arr[1], arr[2]);
            }

            return initializedValues;
        }

        private static async Task<(DropItemForBuildXLFile[], string error)> CreateDropItemsForDirectoryAsync(
            DropDaemon daemon,
            string fullyQualifiedDropName,
            string directoryPath,
            string directoryId,
            string dropPath,
            Regex contentFilter,
            RelativePathReplacementArguments relativePathReplacementArgs)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Requires(!string.IsNullOrEmpty(directoryId));
            Contract.Requires(dropPath != null);

            if (daemon.ApiClient == null)
            {
                return (null, "ApiClient is not initialized");
            }

            DirectoryArtifact directoryArtifact = BuildXL.Ipc.ExternalApi.DirectoryId.Parse(directoryId);

            var maybeResult = await daemon.GetSealedDirectoryContentAsync(directoryArtifact, directoryPath);
            if (!maybeResult.Succeeded)
            {
                return (null, "could not get the directory content from BuildXL server: " + maybeResult.Failure.Describe());
            }

            var directoryContent = maybeResult.Result;
            daemon.Logger.Verbose($"(dirPath'{directoryPath}', dirId='{directoryId}') contains '{directoryContent.Count}' files:{Environment.NewLine}{string.Join(Environment.NewLine, directoryContent.Select(f => f.Render()))}");

            if (contentFilter != null)
            {
                var filteredContent = directoryContent.Where(file => contentFilter.IsMatch(file.FileName)).ToList();
                daemon.Logger.Verbose("[dirId='{0}'] Filter '{1}' excluded {2} file(s) out of {3}", directoryId, contentFilter, directoryContent.Count - filteredContent.Count, directoryContent.Count);
                directoryContent = filteredContent;
            }

            List<DropItemForBuildXLFile> dropItemForBuildXLFiles = new List<DropItemForBuildXLFile>();

            var files = directoryContent
                // SharedOpaque directories might contain 'absent' output files. These are not real files, so we are excluding them.
                .Where(file => !WellKnownContentHashUtilities.IsAbsentFileHash(file.ContentInfo.Hash) || file.Artifact.IsSourceFile);

            foreach (SealedDirectoryFile file in files)
            {
                // We need to convert '\' into '/' because this path would be a part of a drop url
                // The dropPath can be an empty relative path (i.e. '.') which we need to remove since even though it is not a valid
                // directory name for a Windows file system, it is a valid name for a drop and it doesn't get resolved properly
                var resolvedDropPath = dropPath == "." ? string.Empty : I($"{dropPath}/");
                var remoteFileName = I($"{resolvedDropPath}{GetRelativePath(directoryPath, file.FileName, relativePathReplacementArgs).Replace('\\', '/')}");

                dropItemForBuildXLFiles.Add(new DropItemForBuildXLFile(
                    daemon.ApiClient,
                    fullyQualifiedDropName,
                    file.FileName,
                    BuildXL.Ipc.ExternalApi.FileId.ToString(file.Artifact),
                    file.ContentInfo,
                    remoteFileName));
            }

            return (dropItemForBuildXLFiles.ToArray(), null);
        }

        internal static string GetRelativePath(string root, string file, RelativePathReplacementArguments pathReplacementArgs)
        {
            var rootEndsWithSlash =
                root[root.Length - 1] == System.IO.Path.DirectorySeparatorChar
                || root[root.Length - 1] == System.IO.Path.AltDirectorySeparatorChar;
            var relativePath = file.Substring(root.Length + (rootEndsWithSlash ? 0 : 1));
            // On Windows, file paths are case-insensitive.
            var stringCompareMode = OperatingSystemHelper.IsUnixOS ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (pathReplacementArgs.IsValid)
            {
                int searchStringPosition = relativePath.IndexOf(pathReplacementArgs.OldValue, stringCompareMode);
                if (searchStringPosition < 0)
                {
                    // no match found; return the path that we constructed so far
                    return relativePath;
                }

                // we are only replacing the first match
                return I($"{relativePath.Substring(0, searchStringPosition)}{pathReplacementArgs.NewValue}{relativePath.Substring(searchStringPosition + pathReplacementArgs.OldValue.Length)}");
            }

            return relativePath;
        }

        private static async Task<(IEnumerable<DropItemForBuildXLFile>, string error)> CreateDropItemsForDirectoriesAsync(
            DropDaemon daemon,
            string fullyQualifiedDropName,
            string[] directoryPaths,
            string[] directoryIds,
            string[] dropPaths,
            Regex[] contentFilters,
            RelativePathReplacementArguments[] relativePathsReplacementArgs)
        {
            Contract.Requires(directoryPaths != null);
            Contract.Requires(directoryIds != null);
            Contract.Requires(dropPaths != null);
            Contract.Requires(contentFilters != null);
            Contract.Requires(directoryPaths.Length == directoryIds.Length);
            Contract.Requires(directoryPaths.Length == dropPaths.Length);
            Contract.Requires(directoryPaths.Length == contentFilters.Length);
            Contract.Requires(directoryPaths.Length == relativePathsReplacementArgs.Length);

            var createDropItemsTasks = Enumerable
                .Range(0, directoryPaths.Length)
                .Select(i => CreateDropItemsForDirectoryAsync(
                    daemon, fullyQualifiedDropName, directoryPaths[i], directoryIds[i], dropPaths[i], contentFilters[i], relativePathsReplacementArgs[i]))
                .ToArray();

            var createDropItemsResults = await TaskUtilities.SafeWhenAll(createDropItemsTasks);

            if (createDropItemsResults.Any(r => r.error != null))
            {
                return (null, string.Join("; ", createDropItemsResults.Where(r => r.error != null).Select(r => r.error)));
            }

            return (createDropItemsResults.SelectMany(r => r.Item1), null);
        }

        private static (IEnumerable<DropItemForBuildXLFile>, string error) DedupeDropItems(IEnumerable<DropItemForBuildXLFile> dropItems)
        {
            var dropItemsByDropPaths = new Dictionary<string, DropItemForBuildXLFile>(OperatingSystemHelper.PathComparer);

            foreach (var dropItem in dropItems)
            {
                if (dropItemsByDropPaths.TryGetValue(dropItem.RelativeDropPath, out var existingDropItem))
                {
                    if (!string.Equals(dropItem.FullFilePath, existingDropItem.FullFilePath, OperatingSystemHelper.PathComparison))
                    {
                        return (
                          null,
                          I($"'{dropItem.FullFilePath}' cannot be added to drop because it has the same drop path '{dropItem.RelativeDropPath}' as '{existingDropItem.FullFilePath}'"));
                    }
                }
                else
                {
                    dropItemsByDropPaths.Add(dropItem.RelativeDropPath, dropItem);
                }
            }

            return (dropItemsByDropPaths.Select(kvp => kvp.Value).ToArray(), null);
        }

        private static async Task<IIpcResult> AddDropItemsAsync(DropDaemon daemon, IEnumerable<DropItemForBuildXLFile> dropItems)
        {
            (IEnumerable<DropItemForBuildXLFile> dedupedDropItems, string error) = DedupeDropItems(dropItems);

            if (error != null)
            {
                return new IpcResult(IpcResultStatus.ExecutionError, error);
            }

            var ipcResultTasks = dedupedDropItems.Select(d => daemon.AddFileAsync(d)).ToArray();

            var ipcResults = await TaskUtilities.SafeWhenAll(ipcResultTasks);

            return IpcResult.Merge(ipcResults);
        }

        /// <summary>
        /// Creates an IPC client using the config from a ConfiguredCommand
        /// </summary>
        public static IClient CreateClient(ConfiguredCommand conf)
        {
            var daemonConfig = CreateDaemonConfig(conf);
            return IpcProvider.GetClient(daemonConfig.Moniker, daemonConfig);
        }

        /// <summary>
        /// Gets the content of a SealedDirectory from BuildXL and caches the result
        /// </summary>
        internal Task<Possible<List<SealedDirectoryFile>>> GetSealedDirectoryContentAsync(DirectoryArtifact artifact, string path)
        {
            return m_directoryArtifactContent.GetOrAdd(artifact, (daemon: this, path), static (key, tuple) =>
            {
                return new AsyncLazy<Possible<List<SealedDirectoryFile>>>(() => tuple.daemon.ApiClient.GetSealedDirectoryContent(key, tuple.path));
            }).Item.Value.GetValueAsync();
        }

        internal void RegisterDropClientForTesting(DropConfig config, IDropClient client)
        {
            m_vsoClients.Add(FullyQualifiedDropName(config), (config, new Lazy<Task<IDropClient>>(() => Task.FromResult(client))));
        }

        private void EnsureVsoClientIsCreated(DropConfig dropConfig)
        {
            var name = FullyQualifiedDropName(dropConfig);
            var getOrAddResult = m_vsoClients.GetOrAdd(
                name,
                (logger: m_logger, apiClient: ApiClient, daemonConfig: Config, dropConfig: dropConfig),
                static (dropName, data) =>
                {
                    return (data.dropConfig, new Lazy<Task<IDropClient>>(() => Task.Run(() => (IDropClient)new VsoClient(data.logger, data.apiClient, data.daemonConfig, data.dropConfig))));
                });

            // if it's a freshly added VsoClient, start the task
            if (!getOrAddResult.IsFound)
            {
                getOrAddResult.Item.Value.lazyVsoClientTask.Value.Forget();
            }
        }

        internal static string FullyQualifiedDropName(DropConfig dropConfig) => FullyQualifiedDropName(dropConfig.Service, dropConfig.Name);

        private static string FullyQualifiedDropName(Uri service, string dropName) => $"{service?.ToString() ?? string.Empty}/{dropName}";

        internal readonly struct RelativePathReplacementArguments
        {
            public string OldValue { get; }

            public string NewValue { get; }

            public bool IsValid => OldValue != null && NewValue != null;

            public RelativePathReplacementArguments(string oldValue, string newValue)
            {
                OldValue = oldValue;
                NewValue = newValue;
            }

            public static RelativePathReplacementArguments Invalid => new RelativePathReplacementArguments(null, null);
        }
    }
}
