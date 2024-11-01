﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildToolsInstaller.Config;
using BuildToolsInstaller.Utiltiies;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller
{
    /// <summary>
    /// Installs BuildXL from the well-known feed that is assumed to be mirrored in
    /// the organization that is running the installer. 
    /// </summary>
    public class BuildXLNugetInstaller : IToolInstaller
    {
        private readonly INugetDownloader m_downloader;
        private readonly ILogger m_logger;
        private BuildXLNugetInstallerConfig? m_config;

        private static string PackageName => OperatingSystem.IsWindows() ? "BuildXL.win-x64" : "BuildXL.linux-x64";

        public BuildXLNugetInstaller(INugetDownloader downloader, ILogger logger)
        {
            m_downloader = downloader;
            m_logger = logger;
        }

        /// <inheritdoc />
        public async Task<bool> InstallAsync(BuildToolsInstallerArgs args)
        {
            if (!await TryInitializeConfigAsync(args))
            {
                return false;
            }

            try
            {
                var version = m_config!.Version ?? await TryResolveVersionAsync();
                if (version == null)
                {
                    m_logger.Error("BuildLXNugetInstaller: failed to resolve version to install.");
                    return false;
                }

                var downloadLocation = GetCachedToolRootDirectory(args.ToolsDirectory);
                var engineLocation = GetDownloadLocation(args.ToolsDirectory, version);
                if (Path.Exists(engineLocation))
                {
                    // TODO: Can we use the Nuget cache to handle this instead of doing this naive existence check?

                    m_logger.Info($"BuildXL version {version} already installed at {engineLocation}.");

                    if (args.ForceInstallation)
                    {
                        m_logger.Info($"Installation is forced. Deleting {engineLocation} and re-installing.");
                        
                        // Delete the contents of the installation directory and continue
                        if (!TryDeleteInstallationDirectory(engineLocation))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        m_logger.Info($"Skipping download");
                        SetLocationVariable(engineLocation);
                        return true;
                    }
                }

                if (!NuGetVersion.TryParse(version, out var nugetVersion))
                {
                    m_logger.Error($"The provided version for BuildXL package is malformed: {m_config.Version}.");
                    return false;
                }

                var feed = m_config.FeedOverride ?? InferSourceRepository();

                var repository = CreateSourceRepository(feed);
                if (await m_downloader.TryDownloadNugetToDiskAsync(repository, PackageName, nugetVersion, downloadLocation, m_logger))
                {
                    SetLocationVariable(engineLocation);
                    return true;
                }
            }
            catch (Exception ex)
            {
                m_logger.Error($"Failed trying to download nuget package '{PackageName}' : '{ex}'");
            }

            return false;
        }

        private bool TryDeleteInstallationDirectory(string engineLocation)
        {
            try
            {
                Directory.Delete(engineLocation, true);
            }
            catch (Exception e)
            {
                m_logger.Error(e, "Couldn't delete pre-existing installation directory {engineLocation}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Construct the implicit source repository for installers, a well-known feed that should be installed in the organization
        /// </summary>
        private static string InferSourceRepository()
        {
            if (!AdoUtilities.IsAdoBuild)
            {
                throw new InvalidOperationException("Automatic source repository inference is only supported when running on an ADO Build");
            }

            if (!AdoUtilities.TryGetOrganizationName(out var adoOrganizationName))
            {
                throw new InvalidOperationException("Could not retrieve organization name");
            }

            // This feed is installed in every organization as part of 1ESPT onboarding,
            // so we can assume its existence in this context, but we also assume throughout
            // that this feed will upstream the relevant feeds needed to acquire BuildXL
            // (as this set-up should be a part of the onboarding to 'BuildXL on 1ESPT').
            return $"https://pkgs.dev.azure.com/{adoOrganizationName}/_packaging/Guardian1ESPTUpstreamOrgFeed/nuget/v3/index.json";
        }

        /// <nodoc />
        private static SourceRepository CreateSourceRepository(string feedUrl)
        {
            var packageSource = new PackageSource(feedUrl, "SourceFeed");

            // Because the feed is known (either the well-known mirror or the user-provided override),
            // we can simply use a PAT that we assume will grant the appropriate privileges instead of going through a credential provider.
            packageSource.Credentials = new PackageSourceCredential(feedUrl, "IrrelevantUsername", GetPatFromEnvironment(), true, string.Empty);
            return Repository.Factory.GetCoreV3(packageSource);
        }

        private void SetLocationVariable(string engineLocation)
        {
            AdoUtilities.SetVariable("ONEES_BUILDXL_LOCATION", engineLocation, isReadOnly: true);
        }

        private async Task<bool> TryInitializeConfigAsync(BuildToolsInstallerArgs args)
        {
            if (args.ConfigFilePath == null)
            {
                m_config = new BuildXLNugetInstallerConfig();
                return true;
            }

            m_config = await JsonDeserializer.DeserializeAsync<BuildXLNugetInstallerConfig>(args.ConfigFilePath, m_logger, CancellationToken.None);
            if (m_config == null)
            {
                m_logger.Error("Could not parse the BuildXL installer configuration. Installation will fail.");
                return false;
            }

            return true;
        }

        internal static string GetDownloadLocation(string toolDirectory, string version) => Path.Combine(GetCachedToolRootDirectory(toolDirectory), $"{PackageName}.{version}");
        private static string GetCachedToolRootDirectory(string toolDirectory) => Path.Combine(toolDirectory, "BuildXL", "x64");

        private static string GetPatFromEnvironment()
        {
            return Environment.GetEnvironmentVariable("BUILDTOOLSDOWNLOADER_NUGET_PAT") ?? Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN") ?? "";
        }

        private async Task<string?> TryResolveVersionAsync()
        {
            const string ConfigurationWellKnownUri = "https://bxlscripts.z20.web.core.windows.net/config/buildxl/BuildXLConfig_V0.json";
            var jsonUri = new Uri(m_config!.Internal_GlobalConfigOverride ?? ConfigurationWellKnownUri);
            var config = await JsonDeserializer.DeserializeFromHttpAsync<BuildXLGlobalConfig_V0>(jsonUri, m_logger, default);
            if (config == null)
            {
                // Error should have been logged.
                return null;
            }

            return config.Release;
        }
    }
}
