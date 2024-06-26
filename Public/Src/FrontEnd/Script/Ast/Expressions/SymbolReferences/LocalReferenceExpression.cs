// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities.Core;
using static BuildXL.Utilities.Core.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Reference to a local variable or to a function argument.
    /// </summary>
    public class LocalReferenceExpression : SymbolReferenceExpression
    {
        /// <summary>
        /// Index in the local argument table.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Name of the argument or local variable.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <nodoc />
        public LocalReferenceExpression(SymbolAtom name, int index, LineInfo location)
            : base(location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(index >= 0);

            Name = name;
            Index = index;
        }

        /// <nodoc />
        public LocalReferenceExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            Index = reader.ReadInt32Compact();
            Name = reader.ReadSymbolAtom();
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.WriteCompact(Index);
            writer.Write(Name);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.LocalReferenceExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return ToDebugString(Name);
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
#if DEBUG
            // By construction this condition must hold.
            Contract.Assume(frame.Length > Index);
            Contract.Assume(frame[Index].Value != null, I($"frame[{Index}] should have a value, but is null"));
#endif

            return frame[Index];
        }
    }
}
