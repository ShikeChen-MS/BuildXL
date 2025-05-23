// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Tracing;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpPipAnalyzer()
        {
            string outputFilePath = null;
            long semiStableHash = 0;
            bool useOriginalPaths = false;
            bool launchHtmlViewer = false;
            DirectoryArtifact outputDirectory = default;
            bool includeStaticMembers = false;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else if (opt.Name.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    semiStableHash = ParseSemistableHash(opt);
                }
                else if (opt.Name.Equals("useOriginalPaths", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("u", StringComparison.OrdinalIgnoreCase))
                {
                    useOriginalPaths = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("directory", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("d", StringComparison.OrdinalIgnoreCase))
                {
                    outputDirectory = parseDirectoryArtifact(opt);
                }
                else if (opt.Name.Equals("launchHtmlViewer", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    launchHtmlViewer = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("includeStaticMembers", StringComparison.OrdinalIgnoreCase) ||
                         opt.Name.Equals("i", StringComparison.OrdinalIgnoreCase))
                {
                    includeStaticMembers = ParseBooleanOption(opt);
                }
                else
                {
                    throw Error("Unknown option for dump pip analysis: {0}", opt.Name);
                }
            }

            if ((semiStableHash == 0) == (!outputDirectory.IsValid))
            {
                throw Error("Either /pip or /directory parameter must be specified");
            }

            return new DumpPipAnalyzer(GetAnalysisInput(), outputFilePath, launchHtmlViewer, semiStableHash, outputDirectory, useOriginalPaths, includeStaticMembers);

            DirectoryArtifact parseDirectoryArtifact(Option opt)
            {
                if (!DumpPipAnalyzer.TryDeserializeDirectoryArtifact(opt.Value, out DirectoryArtifact directory) || !directory.IsValid)
                {
                    throw Error("Invalid directory artifact: {0}.", opt.Value);
                }

                return directory;
            }
        }

        private static void WriteDumpPipAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump Pip Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.DumpPip), "Generates an html file containing information about the requested pip");
            writer.WriteOption("outputFile", "Required. The location of the output file for critical path analysis.", shortName: "o");
            writer.WriteOption("pip", "Required. The formatted semistable hash of a pip to dump (must start with 'Pip', e.g., 'PipC623BCE303738C69')");
            writer.WriteOption("includeStaticMembers", "Optional. For directory dependencies print the statically known members (only applies to Full/Partial sealed directories)");
        }
    }

    /// <summary>
    /// Exports a JSON structured graph, including per-pip static and execution details.
    /// </summary>
    public sealed class DumpPipAnalyzer : Analyzer
    {
        private readonly Pip m_pip;
        private readonly HtmlHelper m_html;

        private readonly string m_outputFilePath;
        private readonly bool m_launchHtmlViewer;

        private readonly List<XElement> m_sections = new List<XElement>();
        private readonly Dictionary<ModuleId, string> m_moduleIdToFriendlyName = new Dictionary<ModuleId, string>();
        private readonly ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>> m_directoryContents = new ConcurrentBigMap<DirectoryArtifact, IReadOnlyList<FileArtifact>>();

        private BxlInvocationEventData m_invocationData;
        private readonly bool m_useOriginalPaths;
        private readonly bool m_includeStaticMembers;

        public DumpPipAnalyzer(AnalysisInput input, string outputFilePath, bool launchHtmlViewer, long semiStableHash, DirectoryArtifact directory, bool useOriginalPaths, bool includeStaticMembers, bool logProgress = false)
            : base(input)
        {
            if (string.IsNullOrEmpty(outputFilePath))
            {
                outputFilePath = Path.Combine(Path.GetDirectoryName(input.ExecutionLogPath), $"Pip{semiStableHash:X16}.html");
                Console.WriteLine($"Missing option /outputFilePath using: {outputFilePath}");
            }

            m_outputFilePath = outputFilePath;
            m_launchHtmlViewer = launchHtmlViewer;
            m_useOriginalPaths = useOriginalPaths;
            m_includeStaticMembers = includeStaticMembers;

            if (logProgress)
            {
                Console.WriteLine("Finding matching pip");
            }

            var pipTable = input.CachedGraph.PipTable;
            foreach (var pipId in pipTable.StableKeys)
            {
                if (pipTable.GetPipType(pipId) == PipType.Module)
                {
                    var modulePip = (ModulePip)pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                    m_moduleIdToFriendlyName.Add(modulePip.Module, modulePip.Identity.ToString(StringTable));
                }

                var possibleMatch = pipTable.GetPipSemiStableHash(pipId);
                if (possibleMatch == semiStableHash)
                {
                    m_pip = pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                }
            }

            if (directory.IsValid)
            {
                Console.WriteLine("Looking for a pip that produced the specified DirectoryArtifact.");

                var directoryProducers = input.CachedGraph.PipGraph.AllOutputDirectoriesAndProducers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (directoryProducers.TryGetValue(directory, out var pipId))
                {
                    m_pip = pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                }
                // This directory artifact does not have a registered producer. This might happen if it represents a composite SOD.
                else if (directory.IsSharedOpaque)
                {
                    directoryProducers = input.CachedGraph.PipGraph.AllCompositeSharedOpaqueDirectoriesAndProducers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    if (directoryProducers.TryGetValue(directory, out pipId))
                    {
                        m_pip = pipTable.HydratePip(pipId, PipQueryContext.ViewerAnalyzer);
                    }
                }
            }

            if (m_pip == null)
            {
                throw CommandLineUtilities.Error("Did not find a matching pip.");
            }

            m_html = new HtmlHelper(PathTable, StringTable, SymbolTable, CachedGraph.PipTable);
        }

        public XDocument GetXDocument()
        {
            var basicRows = new List<object>();
            basicRows.Add(m_html.CreateRow("PipId", m_pip.PipId.Value.ToString(CultureInfo.InvariantCulture) + " (" + m_pip.PipId.Value.ToString("X16", CultureInfo.InvariantCulture) + ")"));
            basicRows.Add(m_html.CreateRow("SemiStableHash", m_pip.SemiStableHash.ToString("X16")));
            basicRows.Add(m_html.CreateRow("Pip Type", m_pip.PipType.ToString()));
            basicRows.Add(m_html.CreateRow("Tags", m_pip.Tags.IsValid ? m_pip.Tags.Select(tag => tag.ToString(StringTable)) : null));

            var provenance = m_pip.Provenance;
            if (provenance != null)
            {
                basicRows.Add(m_html.CreateRow("Qualifier", PipGraph.Context.QualifierTable.GetCanonicalDisplayString(provenance.QualifierId)));
                basicRows.Add(m_html.CreateRow("Usage", provenance.Usage));
                basicRows.Add(m_html.CreateRow("Spec", provenance.Token.Path));
                basicRows.Add(m_html.CreateRow("Location", provenance.Token));
                basicRows.Add(m_html.CreateRow("Thunk", provenance.OutputValueSymbol));
                basicRows.Add(m_html.CreateRow("ModuleId", GetModuleName(provenance.ModuleId)));
            }


            var main = new XElement(
                "div",
                new XElement(
                    "div",
                    m_html.CreateBlock(
                        "Pip Metadata",
                        basicRows),
                    GetPipSpecificDetails(m_pip),
                    m_html.CreateBlock(
                        "Static Pip Dependencies",
                        m_html.CreateRow(
                            "Pip Dependencies",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependencies(m_pip.PipId, null)
                                .Where(pipRef => pipRef.PipType != PipType.HashSourceFile)
                                .Select(pipRef => pipRef.PipId)),
                        m_html.CreateRow(
                            "Source Dependencies",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependencies(m_pip.PipId, null)
                                .Where(pipRef => pipRef.PipType == PipType.HashSourceFile)
                                .Select(pipRef => pipRef.PipId)),
                        m_html.CreateRow(
                            "Pip Dependents",
                            CachedGraph
                                .PipGraph
                                .RetrievePipReferenceImmediateDependents(m_pip.PipId, null)
                                .Select(pipRef => pipRef.PipId))),
                    m_sections));

            var doc = m_html.CreatePage("Pip Details for Pip" + m_pip.SemiStableHash.ToString("X16"), main);
            return doc;
        }

        public override int Analyze()
        {
            var doc = GetXDocument();
            if (doc == null)
            {
                return 1;
            }

            var loggingConfig = m_invocationData.Configuration.Logging;

            if (m_useOriginalPaths &&
                loggingConfig.SubstTarget.IsValid &&
                loggingConfig.SubstSource.IsValid
                )
            {
                // tostring on root of drive automatically adds trailing slash, so only add trailing slash when needed.
                var target = loggingConfig.SubstTarget.ToString(PathTable, PathFormat.HostOs);
                if (target.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    target += Path.DirectorySeparatorChar;
                }

                var source = loggingConfig.SubstSource.ToString(PathTable, PathFormat.HostOs);
                if (source.LastOrDefault() != Path.DirectorySeparatorChar)
                {
                    source += Path.DirectorySeparatorChar;
                }

                // Instead of doing the proper replacement at every path emission, taking a very blunt shortcut here by doing string replace.
                var html = doc.ToString();
                var updatedHtml = html.Replace(target, source);
                File.WriteAllText(m_outputFilePath, updatedHtml);
                return 0;
            }

            doc.Save(m_outputFilePath);
            if (m_launchHtmlViewer) {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(m_outputFilePath)
                    {
                        UseShellExecute = true
                    }
                );
            }

            return 0;
        }

        public override void BxlInvocation(BxlInvocationEventData data)
        {
            m_invocationData = data;
        }

        public override void PipExecutionPerformance(PipExecutionPerformanceEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Pip Execution Performance",
                        m_html.CreateRow("Execution Start", data.ExecutionPerformance.ExecutionStart),
                        m_html.CreateRow("Execution Stop", data.ExecutionPerformance.ExecutionStop),
                        m_html.CreateRow("WorkerId", data.ExecutionPerformance.WorkerId.ToString(CultureInfo.InvariantCulture)),
                        m_html.CreateEnumRow("ExecutionLevel", data.ExecutionPerformance.ExecutionLevel)));
            }
        }

        public override void PipExecutionStepPerformanceReported(PipExecutionStepPerformanceEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Pip Execution Step Performance Event Data",
                        m_html.CreateRow("StartTime", data.StartTime),
                        m_html.CreateRow("Duration", data.Duration),
                        m_html.CreateEnumRow("Step", data.Step),
                        m_html.CreateEnumRow("Dispatcher", data.Dispatcher)));
            }
        }

        public override void ProcessExecutionMonitoringReported(ProcessExecutionMonitoringReportedEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Process Execution Monitoring",
                        m_html.CreateRow("ReportedProcesses", new XElement("div", data.ReportedProcesses.Select(RenderReportedProcess))),
                        m_html.CreateRow("ReportedFileAcceses", new XElement("div", data.ReportedFileAccesses.Select(RenderReportedFileAccess))),
                        m_html.CreateRow("AllowlistedReportedFileAccesses", new XElement("div", data.AllowlistedReportedFileAccesses.Select(RenderReportedFileAccess))),
                        m_html.CreateRow("ProcessDetouringStatuses", new XElement("div", data.ProcessDetouringStatuses.Select(RenderProcessDetouringStatusData)))));
            }
        }

        private XElement RenderReportedFileAccess(ReportedFileAccess data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("Path", data.Path) ?? m_html.CreateRow("Path", data.ManifestPath),
                m_html.CreateRow("RequestedAccess", RequestedAccessToString(data.RequestedAccess)),
                m_html.CreateEnumRow("CreationDisposition", data.CreationDisposition),
                m_html.CreateEnumRow("DesiredAccess", data.DesiredAccess),
                m_html.CreateEnumRow("ShareMode", data.ShareMode),
                m_html.CreateEnumRow("Status", data.Status),
                m_html.CreateEnumRow("RequestedAccess", data.RequestedAccess),
                m_html.CreateEnumRow("Operation", data.Operation),
                m_html.CreateEnumRow("FlagsAndAttributes", data.FlagsAndAttributes),
                m_html.CreateRow("Error", data.Error.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("RawError", data.RawError.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Usn", data.Usn.Value.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ManifestPath", data.ManifestPath),
                m_html.CreateRow("Process", data.Process.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ExplicitlyReported", data.ExplicitlyReported),
                m_html.CreateRow("EnumeratePattern", data.EnumeratePattern));
        }

        private static string RequestedAccessToString(RequestedAccess requestedAccess)
        {
            switch (requestedAccess)
            {
                case RequestedAccess.Enumerate:
                case RequestedAccess.EnumerationProbe:
                    return "[Enumerate]";
                case RequestedAccess.Probe:
                    return "[Probe]";
                case RequestedAccess.Read:
                    return "[Read]";
                case RequestedAccess.Write:
                    return "[Write]";
            }

            return string.Empty;
        }

        private XElement RenderReportedProcess(ReportedProcess data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("ProcessId", data.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ParentProcessId", data.ParentProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Path", data.Path),
                m_html.CreateRow("ProcessArgs", data.ProcessArgs),
                m_html.CreateRow("CreationTime", data.CreationTime),
                m_html.CreateRow("ExitTime", data.ExitTime),
                m_html.CreateRow("ExitCode", data.ExitCode.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("KernelTime", data.KernelTime),
                m_html.CreateRow("UserTime", data.UserTime),
                m_html.CreateRow("IOCounters.Read", PrintIoTypeCounters(data.IOCounters.ReadCounters)),
                m_html.CreateRow("IOCounters.Write", PrintIoTypeCounters(data.IOCounters.WriteCounters)),
                m_html.CreateRow("IOCounters.Other", PrintIoTypeCounters(data.IOCounters.OtherCounters)));
        }

        private XElement RenderProcessDetouringStatusData(ProcessDetouringStatusData data)
        {
            return new XElement(
                "div",
                new XAttribute("class", "miniGroup"),
                m_html.CreateRow("ProcessId", data.ProcessId.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("JobId", data.Job.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ReportStatus", data.ReportStatus.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("ProcessName", data.ProcessName),
                m_html.CreateRow("StartApplicationName", data.StartApplicationName),
                m_html.CreateRow("StartCommandLine", data.StartCommandLine),
                m_html.CreateRow("NeedsInjection", data.NeedsInjection),
                m_html.CreateRow("NeedsRemoteInjection", data.NeedsRemoteInjection),
                m_html.CreateRow("IsCurrent64BitProcess", data.IsCurrent64BitProcess),
                m_html.CreateRow("IsCurrentWow64Process", data.IsCurrentWow64Process),
                m_html.CreateRow("IsProcessWow64", data.IsProcessWow64),
                m_html.CreateRow("DisableDetours", data.DisableDetours),
                m_html.CreateRow("CreationFlags", data.CreationFlags.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("Detoured", data.Detoured),
                m_html.CreateRow("Error", data.Error.ToString(CultureInfo.InvariantCulture)),
                m_html.CreateRow("CreateProcessStatusReturn", data.CreateProcessStatusReturn.ToString(CultureInfo.InvariantCulture)));
        }

        private string PrintIoTypeCounters(IOTypeCounters counters)
        {
            return $"opCount: {counters.OperationCount}, transferCount: {counters.TransferCount}";
        }

        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Process Fingerprint Computed",
                        m_html.CreateEnumRow("Kind", data.Kind),
                        m_html.CreateRow("WeakContentFingerprintHash", data.WeakFingerprint.Hash.ToHex()),
                        m_html.CreateRow("StrongFingerprintComputations", "TODO")));
            }
        }

        public override void ObservedInputs(ObservedInputsEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Observed Inputs",
                        m_html.CreateRow(
                            "Observed Inputs",
                            new XElement(
                                "div",
                                data.ObservedInputs.Select(
                                    oi =>
                                        new XElement(
                                            "div",
                                            new XAttribute("class", "miniGroup"),
                                            m_html.CreateRow("Path", oi.Path),
                                            m_html.CreateEnumRow("Type", oi.Type),
                                            m_html.CreateRow("Hash", oi.Hash.ToHex()),
                                            m_html.CreateRow("IsSearchpath", oi.IsSearchPath),
                                            m_html.CreateRow("IsDirectoryPath", oi.IsDirectoryPath),
                                            m_html.CreateRow("DirectoryEnumeration", oi.DirectoryEnumeration)))))));
            }
        }

        public override void DependencyViolationReported(DependencyViolationEventData data)
        {
            if (data.ViolatorPipId == m_pip.PipId || data.RelatedPipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Dependecy Violation",
                        m_html.CreateRow("Violator", data.ViolatorPipId),
                        m_html.CreateRow("Related", data.RelatedPipId),
                        m_html.CreateEnumRow("ViolationType", data.ViolationType),
                        m_html.CreateEnumRow("AccessLevel", data.AccessLevel),
                        m_html.CreateRow("Path", data.Path)));
            }
        }

        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            if (data.PipId == m_pip.PipId)
            {
                m_sections.Add(
                    m_html.CreateBlock(
                        "Directory Membership Hashed",
                        m_html.CreateRow("Directory", data.Directory),
                        m_html.CreateRow("IsSearchPath", data.IsSearchPath),
                        m_html.CreateRow("IsStatic", data.IsStatic),
                        m_html.CreateRow("EnumeratePatternRegex", data.EnumeratePatternRegex),
                        m_html.CreateRow("Members", data.Members)));
            }
        }

        public override void PipExecutionDirectoryOutputs(PipExecutionDirectoryOutputs data)
        {
            foreach (var item in data.DirectoryOutputs)
            {
                m_directoryContents[item.directoryArtifact] = item.fileArtifactArray;
            }
        }

        private XElement GetPipSpecificDetails(Pip pip)
        {
            switch (pip.PipType)
            {
                case PipType.CopyFile:
                    return GetCopyFileDetails((CopyFile)pip);
                case PipType.Process:
                    return GetProcessDetails((Process)pip);
                case PipType.Ipc:
                    return GetIpcPipDetails((IpcPip)pip);
                case PipType.Value:
                    return GetValuePipDetails((ValuePip)pip);
                case PipType.SpecFile:
                    return GetSpecFilePipDetails((SpecFilePip)pip);
                case PipType.Module:
                    return GetModulePipDetails((ModulePip)pip);
                case PipType.HashSourceFile:
                    return GetHashSourceFileDetails((HashSourceFile)pip);
                case PipType.SealDirectory:
                    return GetSealDirectoryDetails((SealDirectory)pip);
                case PipType.WriteFile:
                    return GetWriteFileDetails((WriteFile)pip);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private XElement GetWriteFileDetails(WriteFile pip)
        {
            return m_html.CreateBlock(
                "WriteFile Pip Details",
                m_html.CreateRow("Contents", pip.Contents),
                m_html.CreateRow("File Encoding", pip.Encoding.ToString()));
        }

        private XElement GetCopyFileDetails(CopyFile pip)
        {
            return m_html.CreateBlock(
                "CopyFile Pip Details",
                m_html.CreateRow("Source", pip.Source),
                m_html.CreateRow("Destination", pip.Destination));
        }

        private XElement GetProcessDetails(Process pip)
        {
            return new XElement(
                "div",

                m_html.CreateBlock(
                    "Process Invocation Details",
                    m_html.CreateRow("Executable", pip.Executable),
                    m_html.CreateRow("Tool Description", pip.ToolDescription),
                    m_html.CreateRow("Arguments", pip.Arguments),
                    m_html.CreateRow("ResponseFile Path", pip.ResponseFile),
                    m_html.CreateRow("ResponseFile Contents", pip.ResponseFileData),
                    m_html.CreateRow("Environment Variables", new XElement(
                        "table",
                        pip.EnvironmentVariables.Select(envVar => new XElement(
                            "tr",
                            new XElement("td", envVar.Name.ToString(StringTable)),
                            new XElement("td", envVar.Value.IsValid ? envVar.Value.ToString(PathTable) : "[PassThroughValue]"),
                            new XElement("td", $"IsPassThrough:{envVar.IsPassThrough}")))))),

                m_html.CreateBlock(
                    "Process in/out handling",
                    m_html.CreateRow("StdIn File", pip.StandardInput.File),
                    m_html.CreateRow("StdIn Data", pip.StandardInput.Data),
                    m_html.CreateRow("StdOut", pip.StandardOutput),
                    m_html.CreateRow("StdErr", pip.StandardError),
                    m_html.CreateRow("TraceFile", pip.TraceFile),
                    m_html.CreateRow("Std Directory", pip.StandardDirectory),
                    m_html.CreateRow("Warning RegEx", pip.WarningRegex.Pattern),
                    m_html.CreateRow("Error RegEx", pip.ErrorRegex.Pattern)),

                m_html.CreateBlock(
                    "Process Directories",
                    m_html.CreateRow("Working Directory", pip.WorkingDirectory),
                    m_html.CreateRow("Unique Output Directory", pip.UniqueOutputDirectory),
                    m_html.CreateRow("Temporary Directory", pip.TempDirectory),
                    m_html.CreateRow("Extra Temp Directories", pip.AdditionalTempDirectories)),

                m_html.CreateBlock(
                    "Process Advanced option",
                    m_html.CreateRow("Timeout (warning)", pip.WarningTimeout?.ToString()),
                    m_html.CreateRow("Timeout (error)", pip.Timeout?.ToString()),
                    m_html.CreateRow("Success Codes", pip.SuccessExitCodes.Select(code => code.ToString(CultureInfo.InvariantCulture))),
                    m_html.CreateRow("Semaphores", pip.Semaphores.Select(CreateSemaphore)),
                    m_html.CreateRow("PreserveOutputTrustLevel", pip.PreserveOutputsTrustLevel),
                    m_html.CreateRow("PreserveOutputsAllowlist", pip.PreserveOutputAllowlist.Select(allowed => allowed.ToString(PathTable))),
                    m_html.CreateRow("ProcessOptions", pip.ProcessOptions.ToString()),
                    m_html.CreateRow("RewritePolicy", pip.RewritePolicy.ToString()),
                    m_html.CreateRow("RetryExitCodes", string.Join(",", pip.RetryExitCodes)),
                    GetReclassificationRulesDetails(pip.ReclassificationRules),
                    (pip.ProcessRetries != null ? m_html.CreateRow("ProcessRetries", pip.ProcessRetries.Value) : null)),
                    m_html.CreateRow("UncacheableExitCodes", string.Join(",", pip.UncacheableExitCodes)),


                m_html.CreateBlock(
                    "Process inputs/outputs",
                    m_html.CreateRow("File Dependencies", pip.Dependencies),
                    m_html.CreateRow("Directory Dependencies", GetDirectoryDependencies(pip.DirectoryDependencies), sortEntries: false),
                    m_html.CreateRow("Pip Dependencies", pip.OrderDependencies),
                    m_html.CreateRow("File Outputs", pip.FileOutputs.Select(output => output.Path.ToString(PathTable) + " (" + Enum.Format(typeof(FileExistence), output.FileExistence, "f") + ")")),
                    m_html.CreateRow("Directory Outputs", GetDirectoryOutputsWithContent(pip), sortEntries: false),
                    m_html.CreateRow("Untracked Paths", pip.UntrackedPaths),
                    m_html.CreateRow("Untracked Scopes", pip.UntrackedScopes)),

                m_html.CreateBlock(
                    "Global Dependencies",
                    (pip.RequireGlobalDependencies && m_invocationData.Configuration.Sandbox.GlobalUnsafePassthroughEnvironmentVariables != null ?
                        m_html.CreateRow("Passthrough Environment Variables", m_invocationData.Configuration.Sandbox.GlobalUnsafePassthroughEnvironmentVariables) : null),
                    (pip.RequireGlobalDependencies && m_invocationData.Configuration.Sandbox.GlobalUntrackedScopes != null ?
                        m_html.CreateRow("Untracked Scopes", m_invocationData.Configuration.Sandbox.GlobalUntrackedScopes) : null)),

                m_html.CreateBlock(
                    "Service details",
                    m_html.CreateRow("Is Service ", pip.IsService),
                    m_html.CreateRow("ShutdownProcessPipId", pip.ShutdownProcessPipId),
                    m_html.CreateRow("ServicePipDependencies", pip.ServicePipDependencies),
                    m_html.CreateRow("IsStartOrShutdownKind", pip.IsStartOrShutdownKind)));
        }

        private XElement GetReclassificationRulesDetails(ReadOnlyArray<IReclassificationRule> reclassificationRules)
        {
            return m_html.CreateRow(
                "Reclassification rules",
                new XElement(
                    "div",
                    reclassificationRules.Select(
                        (r, i) =>
                            new XElement(
                                "div",
                                new XAttribute("class", "miniGroup"),
                                    m_html.CreateRow("Index", i),
                                    m_html.CreateRow("Name", r.Name),
                                    m_html.CreateRow("Path Regex", r.PathRegex),
                                    m_html.CreateRow("Resolved types", "[ " + string.Join(",", (r.ResolvedObservationTypes?.Select(o => o.ToString()) ?? Array.Empty<string>())) + " ]"),
                                    m_html.CreateRow("Reclassify to", DumpPipLiteAnalysisUtilities.GetReclassifyValue(r.ReclassifyTo))
                            ))));
        }

        private string CreateSemaphore(ProcessSemaphoreInfo semaphore)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} (value:{1} limit:{2})",
                semaphore.Name.ToString(StringTable),
                semaphore.Value,
                semaphore.Limit);
        }

        private XElement GetIpcPipDetails(IpcPip pip)
        {
            return m_html.CreateBlock(
                "IpcPip Details",
                m_html.CreateRow("Ipc MonikerInfo", pip.IpcInfo.IpcMonikerId),
                m_html.CreateRow("MessageBody", pip.MessageBody),
                m_html.CreateRow("OutputFile", pip.OutputFile),
                m_html.CreateRow("ServicePip Dependencies", pip.ServicePipDependencies),
                m_html.CreateRow("File Dependencies", pip.FileDependencies),
                m_html.CreateRow("Directory Dependencies", pip.DirectoryDependencies),
                m_html.CreateRow("LazilyMaterialized File Dependencies", pip.LazilyMaterializedDependencies.Where(a => a.IsFile).Select(a => a.FileArtifact)),
                m_html.CreateRow("LazilyMaterialized Directory Dependencies", pip.LazilyMaterializedDependencies.Where(a => a.IsDirectory).Select(a => a.DirectoryArtifact)),
                m_html.CreateRow("IsServiceFinalization", pip.IsServiceFinalization),
                m_html.CreateRow("MustRunOnOrchestrator", pip.MustRunOnOrchestrator));
        }

        private XElement GetValuePipDetails(ValuePip pip)
        {
            return m_html.CreateBlock(
                "ValuePip Details",
                m_html.CreateRow("Symbol", pip.Symbol),
                m_html.CreateRow("Qualifier", pip.Qualifier.ToString()),
                m_html.CreateRow("SpecFile", pip.LocationData.Path),
                m_html.CreateRow("Location", pip.LocationData));
        }

        private XElement GetSpecFilePipDetails(SpecFilePip pip)
        {
            return m_html.CreateBlock(
                "SpecFilePip Details",
                m_html.CreateRow("SpecFile", pip.SpecFile),
                m_html.CreateRow("Definition File", pip.DefinitionLocation.Path),
                m_html.CreateRow("Definition ", pip.DefinitionLocation),
                m_html.CreateRow("Module", GetModuleName(pip.OwningModule)));
        }

        private XElement GetModulePipDetails(ModulePip pip)
        {
            return m_html.CreateBlock(
                "ModulePip Details",
                m_html.CreateRow("Identity", pip.Identity),
                m_html.CreateRow("Definition File", pip.Location.Path),
                m_html.CreateRow("Definition ", pip.Location));
        }

        private XElement GetHashSourceFileDetails(HashSourceFile pip)
        {
            return m_html.CreateBlock(
                "HashSourceFile Pip Details",
                m_html.CreateRow("Artifact", pip.Artifact));
        }

        private XElement GetSealDirectoryDetails(SealDirectory pip)
        {
            m_directoryContents.TryGetValue(pip.Directory, out var dynamicDirectoryContent);

            return m_html.CreateBlock(
                "SealDirectory Pip Details",
                m_html.CreateEnumRow("Kind", pip.Kind),
                m_html.CreateRow("Scrub", pip.Scrub),
                m_html.CreateRow("DirectoryRoot", pip.Directory),
                m_html.CreateRow("DirectoryArtifact", SerializeDirectoryArtifact(pip.Directory)),
                m_html.CreateRow("ComposedDirectories", pip.ComposedDirectories),
                pip.ContentFilter.HasValue ? m_html.CreateRow("ContentFilter", contentFilterToString(pip.ContentFilter.Value)) : null,
                pip.Patterns.IsValid ? m_html.CreateRow("Patterns", string.Join(",", pip.Patterns.Select(a => a.ToString(StringTable)))) : null,
                m_html.CreateRow("Contents", pip.Contents),
                pip.Kind == SealDirectoryKind.SharedOpaque ? m_html.CreateRow("Dynamic contents", dynamicDirectoryContent) : null);

            string contentFilterToString(SealDirectoryContentFilter filter)
            {
                return $"{filter.Regex} (kind: {Enum.Format(typeof(SealDirectoryContentFilter.ContentFilterKind), filter.Kind, "f")})";
            }
        }

        private string GetModuleName(ModuleId value)
        {
            return value.IsValid ? m_moduleIdToFriendlyName[value] : null;
        }

        private List<string> GetDirectoryOutputsWithContent(Process pip)
        {
            var outputs = new List<string>();
            var rootExpander = new RootExpander(PathTable);

            foreach (var directoryOutput in pip.DirectoryOutputs)
            {
                outputs.Add(FormattableStringEx.I($"{directoryOutput.Path.ToString(PathTable)} (DirectoryArtifact: {SerializeDirectoryArtifact(directoryOutput)}, IsSharedOpaque: {directoryOutput.IsSharedOpaque})"));
                if (m_directoryContents.TryGetValue(directoryOutput, out var directoryContent))
                {
                    foreach (var file in directoryContent)
                    {
                        outputs.Add(FormattableStringEx.I($"|--- {file.Path.ToString(PathTable, rootExpander)}"));
                    }
                }
            }

            return outputs;
        }

        /// <summary>
        /// Returns a properly formatted/sorted list of directory dependencies.
        /// </summary>
        private List<string> GetDirectoryDependencies(ReadOnlyArray<DirectoryArtifact> dependencies)
        {
            var result = new List<string>();
            var directories = new Stack<(DirectoryArtifact artifact, string path, int tabCount)>(
                dependencies
                    .Select(d => (artifact: d, path: d.Path.ToString(PathTable), 0))
                    .OrderByDescending(tupple => tupple.path));

            while (directories.Count > 0)
            {
                var directory = directories.Pop();
                var sealPipId = CachedGraph.PipGraph.GetSealedDirectoryNode(directory.artifact).ToPipId();
                var sealDirectoryKind = PipTable.GetSealDirectoryKind(sealPipId);

                result.Add(directory.tabCount == 0
                    ? FormattableStringEx.I($"{directory.path} (DirectoryArtifact: {SerializeDirectoryArtifact(directory.artifact)}, Kind:{sealDirectoryKind} IsSharedOpaque: {directory.artifact.IsSharedOpaque})")
                    : FormattableStringEx.I($"|{string.Concat(Enumerable.Repeat("---", directory.tabCount))}{directory.path} (DirectoryArtifact: {SerializeDirectoryArtifact(directory.artifact)}, Kind:{sealDirectoryKind}, IsSharedOpaque: {directory.artifact.IsSharedOpaque})"));

                if (PipTable.IsSealDirectoryComposite(sealPipId))
                {
                    var sealPip = (SealDirectory)CachedGraph.PipGraph.GetSealedDirectoryPip(directory.artifact, PipQueryContext.SchedulerExecuteSealDirectoryPip);
                    var isSubDir = sealPip.CompositionActionKind == SealDirectoryCompositionActionKind.NarrowDirectoryCone;
                    foreach (var nestedDirectory in sealPip.ComposedDirectories.Select(d => (artifact: d, path: d.Path.ToString(PathTable))).OrderByDescending(tupple => tupple.path))
                    {
                        directories.Push((nestedDirectory.artifact, $"{(isSubDir ? "subdirectory of " : "")}{nestedDirectory.path}", directory.tabCount + 1));
                    }
                }

                if (m_includeStaticMembers && (sealDirectoryKind == SealDirectoryKind.Partial || sealDirectoryKind == SealDirectoryKind.Full))
                {
                    var sealDirectory = PipTable.HydratePip(sealPipId, PipQueryContext.ViewerAnalyzer) as SealDirectory;

                    result.Add(FormattableStringEx.I($"  Members:"));
                    foreach (var member in sealDirectory.Contents)
                    {
                        result.Add(FormattableStringEx.I($"    {member.Path.ToString(PathTable)}"));
                    }
                }
            }

            return result;
        }

        private static string SerializeDirectoryArtifact(DirectoryArtifact directoryArtifact)
        {
            return $"{directoryArtifact.Path.RawValue}:{directoryArtifact.PartialSealId}:{(directoryArtifact.IsSharedOpaque ? 1 : 0)}";
        }

        internal static bool TryDeserializeDirectoryArtifact(string input, out DirectoryArtifact directoryArtifact)
        {
            var components = input.Split(':');
            if (components.Length != 3)
            {
                directoryArtifact = default;
                return false;
            }

            if (!int.TryParse(components[0], out var pathId)
                || !uint.TryParse(components[1], out var partialSealId)
                || !int.TryParse(components[2], out var isSharedOpaque))
            {
                directoryArtifact = default;
                return false;
            }

            directoryArtifact = new DirectoryArtifact(new AbsolutePath(pathId), partialSealId, isSharedOpaque == 1);
            return true;
        }
    }
}
