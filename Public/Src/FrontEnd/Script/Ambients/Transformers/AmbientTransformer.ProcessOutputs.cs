// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Pips.Builders;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    /// <summary>
    /// Ambient definition for namespace Transformer.
    /// </summary>
    public partial class AmbientTransformerBase : AmbientDefinitionBase
    {
        private CallSignature m_getOutputFileSignature;
        private CallSignature m_getOutputDirectorySignature;
        private CallSignature m_getOutputFilesSignature;
        private CallSignature m_getOutputDirectoriesSignature;
        private CallSignature m_getRequiredOutputFilesSignature;

        private FunctionStatistic m_getOutputFileStatistic;
        private FunctionStatistic m_getOutputDirectoryStatistic;
        private FunctionStatistic m_getOutputDirectoriesStatistic;
        private FunctionStatistic m_getOutputFilesStatistic;
        private FunctionStatistic m_getRequiredOutputFilesStatistic;

        private SymbolAtom ExecuteResultGetOutputFile;
        private SymbolAtom ExecuteResultGetOutputDirectory;
        private SymbolAtom ExecuteResultGetOutputDirectories;
        private SymbolAtom ExecuteResultGetOutputFiles;
        private SymbolAtom ExecuteResultGetRequiredOutputFiles;
        private SymbolAtom CreateServiceResultServiceId;
        private SymbolAtom ExecuteResultProcessOutputs;

        /// <summary>
        /// The name of the object literal key where the original ProcessOutputs object, the resulting object literal produced by Execute, is stored.
        /// </summary>
        /// <remarks>
        /// This field is not actually exposed in DScript but added as a handy way to retrieve the original process outputs object.
        /// The object literal already contains an indirect reference to ProcessOutputs since it is the target of all its closures, so
        /// this extra field shouldn't be significant from a memory footprint standpoint
        /// </remarks>
        public const string ProcessOutputsSymbolName = "processOutputs";

        private void InitializeProcessOutputNames()
        {
            // Execute result.
            ExecuteResultGetOutputFile = Symbol("getOutputFile");
            ExecuteResultGetOutputDirectory = Symbol("getOutputDirectory");
            ExecuteResultGetOutputDirectories = Symbol("getOutputDirectories");
            ExecuteResultGetOutputFiles = Symbol("getOutputFiles");
            ExecuteResultGetRequiredOutputFiles = Symbol("getRequiredOutputFiles");
            CreateServiceResultServiceId = Symbol("serviceId");
            ExecuteResultProcessOutputs = Symbol(ProcessOutputsSymbolName);
        }

        private void InitializeSignaturesAndStatsForProcessOutputs(StringTable stringTable)
        {
            Contract.Requires(stringTable.IsValid());
            m_getOutputFileSignature = CreateSignature(required: RequiredParameters(AmbientTypes.PathType), returnType: AmbientTypes.FileType);
            var name = FunctionStatistic.GetFullNameAsString(
                FunctionStatistic.GetFullName(AmbientName, ExecuteResultGetOutputFile),
                m_getOutputFileSignature,
                stringTable);
            m_getOutputFileStatistic = new FunctionStatistic(AmbientName, ExecuteResultGetOutputFile, m_getOutputFileSignature, stringTable);

            m_getOutputDirectorySignature = CreateSignature(required: RequiredParameters(AmbientTypes.DirectoryType), returnType: AmbientTypes.StaticDirectoryType);
            m_getOutputDirectoryStatistic = new FunctionStatistic(AmbientName, ExecuteResultGetOutputDirectory, m_getOutputDirectorySignature, stringTable);

            m_getOutputDirectoriesSignature = CreateSignature(returnType: AmbientTypes.ArrayType);
            m_getOutputDirectoriesStatistic = new FunctionStatistic(AmbientName, ExecuteResultGetOutputDirectories, m_getOutputDirectoriesSignature, stringTable);

            m_getOutputFilesSignature = CreateSignature(returnType: AmbientTypes.ArrayType);
            m_getOutputFilesStatistic = new FunctionStatistic(AmbientName, ExecuteResultGetOutputFiles, m_getOutputFilesSignature, stringTable);

            m_getRequiredOutputFilesSignature = CreateSignature(returnType: AmbientTypes.ArrayType);
            m_getRequiredOutputFilesStatistic = new FunctionStatistic(AmbientName, ExecuteResultGetRequiredOutputFiles, m_getRequiredOutputFilesSignature, stringTable);
        }

        private ObjectLiteral BuildExecuteOutputs(Context context, ModuleLiteral env, ProcessOutputs processOutputs, bool isService)
        {
            var entry = context.TopStack;

            using (var empty = EvaluationStackFrame.Empty())
            {
                var getOutputFile = new Closure(
                    env,
                    FunctionLikeExpression.CreateAmbient(ExecuteResultGetOutputFile, m_getOutputFileSignature, GetOutputFile, m_getOutputFileStatistic),
                    frame: empty);

                var getOutputDirectory = new Closure(
                    env,
                    FunctionLikeExpression.CreateAmbient(ExecuteResultGetOutputDirectory, m_getOutputDirectorySignature, GetOutputDirectory, m_getOutputDirectoryStatistic),
                    frame: empty);

                var getOutputDirectories = new Closure(
                    env,
                    FunctionLikeExpression.CreateAmbient(ExecuteResultGetOutputDirectories, m_getOutputDirectoriesSignature, GetOutputDirectories, m_getOutputDirectoriesStatistic),
                    frame: empty);

                var getOutputFiles = new Closure(
                    env,
                    FunctionLikeExpression.CreateAmbient(ExecuteResultGetOutputFiles, m_getOutputFilesSignature, GetOutputFiles, m_getOutputFilesStatistic),
                    frame: empty);

                var getRequiredOutputFiles = new Closure(
                    env,
                    FunctionLikeExpression.CreateAmbient(ExecuteResultGetRequiredOutputFiles, m_getRequiredOutputFilesSignature, GetRequiredOutputFiles, m_getRequiredOutputFilesStatistic),
                    frame: empty);

                var bindings = new List<Binding>(isService ? 6 : 5)
                    {
                        new Binding(ExecuteResultGetOutputFile, getOutputFile, location: default),
                        new Binding(ExecuteResultGetOutputDirectory, getOutputDirectory, location: default),
                        new Binding(ExecuteResultGetOutputDirectories, getOutputDirectories, location: default),
                        new Binding(ExecuteResultGetOutputFiles, getOutputFiles, location: default),
                        new Binding(ExecuteResultGetRequiredOutputFiles, getRequiredOutputFiles, location: default),
                        new Binding(ExecuteResultProcessOutputs, new EvaluationResult(processOutputs), location: default),
                    };
                if (isService)
                {
                    bindings.Add(new Binding(CreateServiceResultServiceId, processOutputs.ProcessPipId, location: default));
                }

                return ObjectLiteral.Create(bindings, entry.InvocationLocation, entry.Path);
            }

            // Local functions
            EvaluationResult GetOutputFile(Context contextArg, ModuleLiteral envArg, EvaluationStackFrame args)
            {
                var outputPath = Args.AsPathOrUndefined(args, 0, false);
                if (outputPath.IsValid && processOutputs.TryGetOutputFile(outputPath, out var file))
                {
                    return EvaluationResult.Create(file);
                }

                return EvaluationResult.Undefined;
            }

            EvaluationResult GetOutputDirectory(Context contextArg, ModuleLiteral envArg, EvaluationStackFrame args)
            {
                var outputDir = Args.AsDirectory(args, 0);

                if (outputDir.IsValid && processOutputs.TryGetOutputDirectory(outputDir.Path, out var output))
                {
                    return EvaluationResult.Create(output);
                }

                return EvaluationResult.Undefined;
            }

            EvaluationResult GetOutputDirectories(Context contextArg, ModuleLiteral envArg, EvaluationStackFrame args)
            {
                var outputDirectories = processOutputs.GetOutputDirectories().Select(d => EvaluationResult.Create(d)).ToArray();
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(outputDirectories, entry.InvocationLocation, entry.Path));
            }

            EvaluationResult GetOutputFiles(Context contextArg, ModuleLiteral envArg, EvaluationStackFrame args)
            {
                var outputFiles = processOutputs.GetOutputFiles().Select(f => EvaluationResult.Create(f)).ToArray();
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(outputFiles, entry.InvocationLocation, entry.Path));
            }

            EvaluationResult GetRequiredOutputFiles(Context contextArg, ModuleLiteral envArg, EvaluationStackFrame args)
            {
                var outputFiles = processOutputs.GetRequiredOutputFiles().Select(f => EvaluationResult.Create(f)).ToArray();
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(outputFiles, entry.InvocationLocation, entry.Path));
            }
        }
    }
}
