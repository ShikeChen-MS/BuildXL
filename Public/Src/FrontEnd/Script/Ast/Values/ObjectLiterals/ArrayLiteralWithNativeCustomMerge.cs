// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.Utilities.Core;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// An array literal associated with a custom native merge function
    /// </summary>
    /// <remarks>
    /// This class is here for optimization purposes: for well-known custom merge functions
    /// we don't need to go through closures in DScript.
    /// </remarks>
    public class ArrayLiteralWithNativeCustomMerge : EvaluatedArrayLiteral
    {
        private readonly MergeFunction m_customNativeMergeFunction;

        /// <summary>
        /// Creates array literal from an existing array, adding a custom merge function
        /// </summary>
        public static ArrayLiteralWithNativeCustomMerge Create(ArrayLiteral arrayLiteral, MergeFunction customNativeMergeFunction, LineInfo location, AbsolutePath path)
        {
            Contract.Assert(arrayLiteral != null);
            Contract.Assert(customNativeMergeFunction != null);
            Contract.Assert(path.IsValid);

            var data = new EvaluationResult[arrayLiteral.Length];
            arrayLiteral.Copy(0, data, 0, arrayLiteral.Length);

            return new ArrayLiteralWithNativeCustomMerge(data, customNativeMergeFunction, location, path);
        }

        /// <nodoc />
        protected ArrayLiteralWithNativeCustomMerge(EvaluationResult[] data, MergeFunction customNativeMergeFunction, LineInfo location, AbsolutePath path)
            : base(data, location, path)
        {
            Contract.Requires(customNativeMergeFunction != null);

            m_customNativeMergeFunction = customNativeMergeFunction;
        }

        /// <inheritdoc/>
        protected override MergeFunction TryGetCustomMergeFunction(Context context, EvaluationStackFrame captures) => m_customNativeMergeFunction;

        /// <inheritdoc/>
        protected override MergeFunction GetDefaultMergeFunction(Context context, EvaluationStackFrame captures) => m_customNativeMergeFunction;
    }
}
