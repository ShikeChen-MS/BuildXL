// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine.Tracing;
using BuildXL.Pips.Operations;
using BuildXL.Pips.Graph;
using Test.BuildXL.EngineTestUtilities;
using Xunit;
using Xunit.Abstractions;
using System;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.TestUtilities.Xunit;
using System.Reflection;
using Test.BuildXL.TestUtilities;
using BuildXL.Utilities.Core;
using BuildXL.Native.IO;
using System.Diagnostics.CodeAnalysis;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Uses an MSBuild resolver to schedule and execute pips based on MSBuild.
    /// </summary>
    /// <remarks>
    /// These tests actually execute pips, and are therefore expensive
    /// </remarks>
    public class MsBuildIntegrationTests : MsBuildPipExecutionTestBase
    {
        public MsBuildIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);
            
            const string TestProj2 = "test2.csproj";
            var pathToTestProj2 = R("public", "dir2", TestProj2);

            const string Dirs = "dirs.proj";
            var config = Build()
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1, pathToTestProj2))
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile1.txt"))
                    .AddSpec(pathToTestProj2, CreateWriteFileTestProject("MyFile2.txt", projectReference: pathToTestProj1))
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var pipGraph = engineResult.EngineState.PipGraph;

            // We should find three process pips: the two test projects and 'dirs'
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(3, processPips.Count);

            var testProj1Value = GetValueSymbolFromProjectRelPath(engineResult.EngineState.SymbolTable, engineResult.EngineState.StringTable, pathToTestProj1);
            var testProj1 = processPips.Find(pip => pip.Provenance.OutputValueSymbol == testProj1Value);
            Assert.True(testProj1 != null);

            var testProj2Value = GetValueSymbolFromProjectRelPath(engineResult.EngineState.SymbolTable, engineResult.EngineState.StringTable, pathToTestProj2);
            var testProj2 = processPips.Find(pip => pip.Provenance.OutputValueSymbol == testProj2Value);
            Assert.True(testProj2 != null);

            var dirsValue = GetValueSymbolFromProjectRelPath(engineResult.EngineState.SymbolTable, engineResult.EngineState.StringTable, Dirs);
            var dirs = processPips.Find(pip => pip.Provenance.OutputValueSymbol == dirsValue);
            Assert.True(dirs != null);

            // Let's do some basic graph validation
            // We should be able to reach the first test project from the second one
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj1.PipId.ToNodeId(), testProj2.PipId.ToNodeId(), skipOutOfOrderNodes: true));
            // We should be able to reach both test projects from 'dirs' project
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj1.PipId.ToNodeId(), dirs.PipId.ToNodeId(), skipOutOfOrderNodes: true));
            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(testProj2.PipId.ToNodeId(), dirs.PipId.ToNodeId(), skipOutOfOrderNodes: true));

            // Make sure pips actually ran, and the files to be written are there
            Assert.True(File.Exists(Path.Combine(RelativeSourceRoot, "public", "dir1", OutDir, "MyFile1.txt")));
            Assert.True(File.Exists(Path.Combine(RelativeSourceRoot, "public", "dir2", OutDir, "MyFile2.txt")));
        }

        [Fact]
        public void MSBuildCacheGraphBehavior()
        {
            var testCache = new TestCache();

            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);

            const string TestProj2 = "test2.csproj";
            var pathToTestProj2 = R("public", "dir2", TestProj2);

            const string Dirs = "dirs.proj";
            var config = (CommandLineConfiguration)Build($"fileNameEntryPoints: [ r`{Dirs}` ]")
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1, pathToTestProj2))
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile1.txt"))
                    .AddSpec(pathToTestProj2, CreateWriteFileTestProject("MyFile2.txt", projectReference: pathToTestProj1))
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);
            
            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void ConfigSpecifiedEnvironmentVarIsIsolatedFromProcessEnvironment()
        {
            var testCache = new TestCache();

            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", "dir1", TestProj1);

            const string TestProj2 = "test2.csproj";
            var pathToTestProj2 = R("public", "dir2", TestProj2);

            // Set env var 'Test' to an arbitrary value, but override that value in the main config, so
            // we actually don't depend on it
            Environment.SetEnvironmentVariable("Test", "2");

            const string Dirs = "dirs.proj";
            var config = (CommandLineConfiguration)Build($"fileNameEntryPoints: [ r`{Dirs}` ]", "Map.empty<string, string>().add('Test', '1')")
                    .AddSpec(Dirs, CreateDirsProject(pathToTestProj1, pathToTestProj2))
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile1.txt"))
                    .AddSpec(pathToTestProj2, CreateWriteFileTestProject("MyFile2.txt", projectReference: pathToTestProj1))
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // Change the environment variable. Since we overrode the value, the second
            // build should fetch and reuse the graph from the cache
            Environment.SetEnvironmentVariable("Test", "3");
            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PassthroughVariablesAreHonored(bool isPassThrough)
        {
            var testCache = new TestCache();

            const string TestProj1 = "test1.csproj";
            var pathToTestProj1 = R("public", TestProj1);

            Environment.SetEnvironmentVariable("Test", "originalValue");

            string environment = $"Map.empty<string, (PassthroughEnvironmentVariable | string)>().add('Test', {(isPassThrough ? "Unit.unit()" : "Environment.getStringValue('Test')")})";

            var config = (CommandLineConfiguration)Build($"fileNameEntryPoints: [ r`{pathToTestProj1}` ]", environment)
                    .AddSpec(pathToTestProj1, CreateWriteFileTestProject("MyFile"))
                    .PersistSpecsAndGetConfiguration();

            config.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");
            
            Environment.SetEnvironmentVariable("Test", "modifiedValue");

            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            // If the variable is a passthrough, the change shouldn't affect caching
            if (isPassThrough)
            {
                AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
                AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
            }
            else
            {
                AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
                AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            }
        }

        [Fact]
        public void ProjectThatRemovesAFileDuringEvaluationIsProperlyHandled()
        {
            // This project writes and deletes a file during evaluation
            var mainProject =
@"<Project>
    <PropertyGroup>
        <AddAndRemoveFile>
            $([System.IO.File]::WriteAllText('temp.txt', 'hello'))$([System.IO.File]::Delete('temp.txt'))
        </AddAndRemoveFile>
    </PropertyGroup>
    <Target Name = 'Build'/>
 </Project>";

            var config = Build(environment: new Dictionary<string, string> { ["MSBUILDENABLEALLPROPERTYFUNCTIONS"] = "1" })
                .AddSpec(R("MainProject.proj"), mainProject)
                .PersistSpecsAndGetConfiguration();

            // Even though there will be an access to a file that is not there anymore, we should run to success
            var result = RunEngineWithConfig(config);
            Assert.True(result.IsSuccess);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData("Program.cs", null, "Out.exe")]
        [InlineData("Program.cs", null, "Out.dll")]
        [InlineData("Program.cs", "library", null)]
        // This case should not finish successfully if csc was called directly, but the csc task implementation always 
        // makes sure the output file is specified (and picks the first source file on absence)
        [InlineData("Program.cs", "exe", null)]
        public void ValidateSharedCompilation(string sourceFilename, [AllowNull]string targetType, [AllowNull] string outputAssembly)
        {
            var managedProject = GetCscProject(sourceFilename, targetType, outputAssembly);
            var program = GetHelloWorldProgram();

            var config = (CommandLineConfiguration)Build(useSharedCompilation: true)
                .AddSpec(R("ManagedProject.csproj"), managedProject)
                .AddFile(R("Program.cs"), program)
                .PersistSpecsAndGetConfiguration();

            config.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
            config.Sandbox.LogObservedFileAccesses = true;

            var listener = new FileAccessDetoursListenerCollector(PathTable);

            var result = RunEngineWithConfig(config, detoursListener: listener);
            Assert.True(result.IsSuccess);

            // Let's verify now that accesses were properly compensated. We should see the (single) source
            // file and the output + pdb as outputs
            var allAccesses = listener.GetAllFileAccessPaths().Select(path => path.ToUpperInvariant());
            Assert.True(allAccesses.Any(input => input.Contains(sourceFilename.ToUpperInvariant())));

            // If the output assembly is not specified, there should be an output reported anyway, with a 
            // filename (modulo extension) equal to the source
            if (outputAssembly == null)
            {
                outputAssembly = Path.ChangeExtension(sourceFilename, null);
            }

            Assert.True(allAccesses.Any(output => output.Contains(outputAssembly.ToUpperInvariant())));
            Assert.True(allAccesses.Any(output => output.Contains(Path.ChangeExtension(outputAssembly, ".pdb").ToUpperInvariant())));
        }

        [Fact]
        public void ValidateSharedCompilationWithRelativePathsForReferences()
        {
            var collector = RunManagedCompilation(useSharedCompilation: true, absolutePathToReference: out string absolutePathToReference, out _);
            Assert.True(collector.GetAllFileAccessPaths().Select(path => path.ToUpperInvariant()).Any(input => input.Contains(absolutePathToReference.ToUpperInvariant())));
        }

        [Fact]
        public void ValidateSharedCompilationWithRelativePaths()
        {
            // An empty manifest should be enough
            var manifestPath = Path.Combine(SourceRoot, "app.manifest");
            FileUtilities.CreateDirectory(SourceRoot);
            File.WriteAllText(manifestPath, string.Empty);

            // Let's pass the manifest as a relative path
            var collector = RunManagedCompilation(
                useSharedCompilation: true, 
                absolutePathToReference: out _, 
                usedTempDirectory: out _, 
                additionalArgs: "Win32Manifest='app.manifest'");

            // Verify the access was made against the correct absolute path
            Assert.True(collector.GetAllFileAccessPaths().Select(path => path.ToUpperInvariant()).Any(input => input.Contains(manifestPath.ToUpperInvariant())));
        }

        [Fact]
        public void ValidateSharedCompilationAccessesAgainstNonSharedCompilation()
        {
            // Run the same managed csc call with and without shared compilation
            var sharedAccessCollector = RunManagedCompilation(useSharedCompilation: true, absolutePathToReference: out _, usedTempDirectory: out _);
            var nonSharedAccessCollector = RunManagedCompilation(useSharedCompilation: false, absolutePathToReference: out _, usedTempDirectory: out var tempDirectory);

            AbsolutePath appDataDirectory = AbsolutePath.Create(PathTable, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            // The non-shared accesses should be a subset of the shared ones. Shared access case could be over-reporting since
            // some predicted artifacts may not be accessed at all. This is anyway a conservative check.
            // Observe there might be some differences under the temp directory, since MSBuild generates there some files with random names, so they are different on each execution. We exclude those.
            var diff = nonSharedAccessCollector.GetFileAccessPaths()
                .Except(sharedAccessCollector.GetFileAccessPaths())
                .Where(path => !path.IsWithin(PathTable, tempDirectory) && !path.IsWithin(PathTable, appDataDirectory));

            // There shouldn't be non shared accesses that were not predicted
            XAssert.IsEmpty(diff);
        }

        [Fact]
        public void ValidateResourcesAreReportedWithSharedCompilation()
        {
            string embeddedFilePath = Path.Combine(SourceRoot, "dummy.txt");
            FileUtilities.CreateDirectory(SourceRoot);
            File.WriteAllText(embeddedFilePath, "-");

            var collector = RunManagedCompilation(
                useSharedCompilation: true, 
                out string _, 
                out AbsolutePath _, 
                additionalArgs: $"Resources='{embeddedFilePath}'");

            AbsolutePath dummyPath = AbsolutePath.Create(PathTable, embeddedFilePath);
            var resourceAccesses = collector.GetFileAccessPaths().Where(p => p.Equals(dummyPath));
            XAssert.IsTrue(resourceAccesses.Any());
        }

        private FileAccessDetoursListenerCollector RunManagedCompilation(
            bool useSharedCompilation, 
            out string absolutePathToReference, 
            out AbsolutePath usedTempDirectory, 
            string additionalArgs = null)
        {
            // We pass the executing assembly to the compiler call just as a way to pass an arbitrary valid assembly (and not because there are any required dependencies on it)
            absolutePathToReference = Assembly.GetExecutingAssembly().Location;
            // We just use the assembly name, but add the assembly directory to the collection of additional paths
            var managedProject = GetCscProject(
                "Program.cs", 
                "exe", 
                "Out.exe", 
                $@"References='{Path.GetFileName(absolutePathToReference)}'
                   AdditionalLibPaths='{Path.GetDirectoryName(absolutePathToReference)}'
                   NoStandardLib='false'
                   NoConfig='false' 
                   {additionalArgs ?? string.Empty}");

            var program = GetHelloWorldProgram();

            var config = (CommandLineConfiguration)Build(useSharedCompilation: useSharedCompilation)
                .AddSpec(R("ManagedProject.csproj"), managedProject)
                .AddFile(R("Program.cs"), program)
                .PersistSpecsAndGetConfiguration();

            config.Sandbox.FileSystemMode = FileSystemMode.RealAndMinimalPipGraph;
            config.Sandbox.LogObservedFileAccesses = true;

            var listener = new FileAccessDetoursListenerCollector(PathTable);
            var result = RunEngineWithConfig(config, detoursListener: listener);
            Assert.True(result.IsSuccess);

            // The whole compilation is performed by a single pip
            var process = (Process)result.EngineState.PipGraph.RetrievePipsOfType(PipType.Process).Single();
            usedTempDirectory = process.TempDirectory;

            return listener;
        }
        
        private string GetCscProject(string sourceFilename, string targetType, string outputAssembly, string extraArgs = null)
        {
            // In order to avoid depending on MSBuild SDKs, which are hard to deploy in a self-contained way,
            // we use the real Csc task implementation, but through a custom (incomplete) Csc task that we define here
            return $@"<Project DefaultTargets='Build'>
    <UsingTask TaskName='Csc'
        AssemblyFile = '{PathToCscTaskDll(shouldRunDotNetCoreMSBuild: false)}'/>

  <Target Name='Build'>
    <Csc
        OutputAssembly='{outputAssembly ?? string.Empty}'
        {(targetType != null ? $"TargetType='{targetType}'" : "")}
        EmitDebugInformation='true'
        Sources='{sourceFilename}'
        UseSharedCompilation='true'
        {extraArgs ?? string.Empty}
    />
  </Target>
</Project>";
        }

        private static string GetHelloWorldProgram()
        {
            // A simple hello world program
            return @"
using System;
namespace CscCompilation
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(""Hello World!"");
        }
    }
}";
        }
    }
}
