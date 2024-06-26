// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Execution.Analyzer;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;
using static Test.Tool.Analyzers.AnalyzerTestBase;
using BuildXLConfiguration = BuildXL.Utilities.Configuration;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Tests for <see cref="CacheMissAnalyzer"/>.
    /// The construction and disposal of these tests rely on the fact that 
    /// Xunit uses a unique class instance for each test.
    /// /// </summary>
    public class CacheMissAnalyzerTests : AnalyzerTestBase
    {
        public CacheMissAnalyzerTests(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.CacheMissLegacy;

            string outputDirectory = Path.Combine(TemporaryDirectory, "cachemiss");
            // Set the result file to the file generated by cache miss analyzer
            ResultFileToRead = Path.Combine(outputDirectory, CacheMissAnalyzer.AnalysisFileName);

            ModeSpecificDefaultArgs = new Option[]
            {
                new Option
                {
                    Name = "outputDirectory",
                    Value = outputDirectory
                }
            };
        }

        [Fact]
        public void SourceFileWeakFingerprintMiss()
        {
            FileArtifact srcFile = CreateSourceFile();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);

            ScheduleRunResult cacheHitBuild = RunScheduler().AssertCacheHit(pip.PipId);

            // Modify file to create weak fingerprint miss
            File.WriteAllText(ArtifactToString(srcFile), "asdf");

            ScheduleRunResult cacheMissBuild = RunScheduler().AssertCacheMiss(pip.PipId);

            RunAnalyzer(cacheHitBuild, cacheMissBuild).AssertPipMiss(
                pip, 
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints, 
                ArtifactToString(srcFile));
        }

        [Fact]
        public void DirectoryEnumerationReadOnlyMountStrongFingerprintMiss()
        {
            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));
            Directory.CreateDirectory(ArtifactToString(dir));

            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            ScheduleRunResult buildA = RunScheduler().AssertCacheHit(pip.PipId);

            // Strong fingerprint miss: AbsentPathProbe => DirectoryEnumeration
            // (empty directory enumeration conflates to absent path probe)
            FileArtifact nestedFile = CreateSourceFile(ArtifactToString(dir));
            ScheduleRunResult buildB = RunScheduler().AssertCacheMiss(pip.PipId);

            RunAnalyzer(buildA, buildB).AssertPipMiss(
                pip,
                PipCacheMissType.MissForDescriptorsDueToStrongFingerprints,
                ArtifactToString(dir),
                "AbsentPathProbe");

            // Strong fingerprint miss: Added new file to enumerated directory
            FileArtifact addedFile = CreateSourceFile(ArtifactToString(dir));
            ScheduleRunResult buildC = RunScheduler().AssertCacheMiss(pip.PipId);

            RunAnalyzer(buildB, buildC).AssertPipMiss(
                pip,
                PipCacheMissType.MissForDescriptorsDueToStrongFingerprints,
                ArtifactToString(dir),
                ArtifactToString(addedFile));
        }

        [Fact]
        public void NonCacheableAllowelistPipMiss()
        {
            FileArtifact allowlistFile = CreateSourceFile();
            var entry = new BuildXLConfiguration.Mutable.FileAccessAllowlistEntry()
            {
                Value = "testValue",
                PathFragment = ArtifactToString(allowlistFile),
            };
            Configuration.FileAccessAllowList.Add(entry);

            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(allowlistFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            ScheduleRunResult buildA = RunScheduler().AssertCacheMiss(pip.PipId);
            ScheduleRunResult buildB = RunScheduler().AssertCacheMiss(pip.PipId);

            RunAnalyzer(buildA, buildB).AssertPipMiss(
                pip,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                "disallowed file accesses in the previous build");
        }

        [Fact]
        public void FileAccessViolationUncacheablePipMiss()
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = false;

            FileArtifact unexpectedFile = CreateSourceFile();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.ReadFile(unexpectedFile, doNotInfer: true)
            }).Process;

            ScheduleRunResult buildA = RunScheduler().AssertCacheMiss(pip.PipId);

            // Pip allowed to run successfully, but will not be cached due to file monitoring violations
            ScheduleRunResult buildB = RunScheduler().AssertCacheMiss(pip.PipId);

            RunAnalyzer(buildA, buildB).AssertPipMiss(
                pip, 
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                "lowed file accesses in the previous build");
        }

        /// <summary>
        /// Only analyze pips downstream of an already-analyzed pip miss if /allPips option is specified.
        /// </summary>
        /// <note>
        /// This prevents analysis of any downstream pips, even if they
        /// had a cache miss cause other than the upstream pip being executed.
        /// </note>
        [Fact]
        public void TestAllPipsOption()
        {
            FileArtifact srcA = CreateSourceFile();
            FileArtifact outA = CreateOutputFileArtifact();
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA),
                Operation.WriteFile(outA)
            }).Process;
            
            // Make pipB dependent on pipA
            FileArtifact srcB = CreateSourceFile();
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcB),
                Operation.ReadFile(outA),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);
            ScheduleRunResult buildA = RunScheduler().AssertCacheHit(pipA.PipId, pipB.PipId);

            // Force miss on pipA
            File.WriteAllText(ArtifactToString(srcA), "asdf");
            // Force miss on pipB
            File.WriteAllText(ArtifactToString(srcB), "hjkl");

            ScheduleRunResult buildB = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);

            AnalyzerResult result = RunAnalyzer(buildA, buildB);
            result.AssertPipMiss(
                pipA,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                ArtifactToString(srcA));
            // Don't analyze downstream pip misses
            XAssert.IsFalse(result.FileOutput.Contains(pipB.FormattedSemiStableHash));

            Option allPips = new Option
            {
                Name = "allPips",
            };

            AnalyzerResult allPipsResult = RunAnalyzer(buildA, buildB, additionalArgs: new Option[] { allPips });
            allPipsResult.AssertPipMiss(
                pipA,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                ArtifactToString(srcA));
            // Analyze downstream pips
            allPipsResult.AssertPipMiss(
                pipA,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                ArtifactToString(srcB));
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void IncrementalSchedulingSkippedPipNoFingerprint()
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;

            FileArtifact src = CreateSourceFile();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(pip.PipId);
            // Incremental scheduling will skip executing clean pip
            var buildA = RunScheduler().AssertCacheHit(pip.PipId);

            File.WriteAllText(ArtifactToString(src), "asfd");
            var buildB = RunScheduler().AssertCacheMiss(pip.PipId);

            AnalyzerResult result = RunAnalyzer(buildA, buildB);

            // The pip is in the old graph, but there is no weak fingerprint computation
            // to compare to since it was skipped in the last build
            result.AssertPipMiss(
                pip, 
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                "No fingerprint computation data found to compare");
        }

        [Fact]
        public void FilterSkippedPipNoFingerprint()
        {
            FileArtifact src = CreateSourceFile();

            var pipBuilderA = this.CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilderA.AddTags(Context.StringTable, "pipA");
            Process pipA = SchedulePipBuilder(pipBuilderA).Process;

            // Create independent pipB
            var pipBuilderB = this.CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilderB.AddTags(Context.StringTable, "pipB");
            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);

            Configuration.Filter = "tag='pipB'"; // filter graph to just pipB

            var buildA = RunScheduler().AssertCacheHit(pipB.PipId);

            Configuration.Filter = ""; // reset filter to default
            
            // Cause a miss on pipA and pipB
            File.WriteAllText(ArtifactToString(src), "asdf");
            var buildB = RunScheduler().AssertCacheMiss(pipA.PipId, pipB.PipId);

            AnalyzerResult result = RunAnalyzer(buildA, buildB);
            // Missing fingerprint for pipA because it was filtered out in last build
            result.AssertPipMiss(
                pipA,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                "No fingerprint computation data found to compare");

            // Normal analysis for pipB
            result.AssertPipMiss(
                pipB,
                PipCacheMissType.MissForDescriptorsDueToWeakFingerprints,
                ArtifactToString(src));
        }
    }
}
