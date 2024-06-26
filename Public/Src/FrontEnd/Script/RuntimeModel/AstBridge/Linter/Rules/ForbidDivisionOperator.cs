// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    /// <summary>
    /// Checks that division is not allowed
    /// </summary>
    internal sealed class ForbidDivisionOperator : LanguageRule
    {
        private ForbidDivisionOperator()
        { }

        /// <inheritdoc />
        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.All;

        public static ForbidDivisionOperator CreateAndRegister(AnalysisContext context)
        {
            var result = new ForbidDivisionOperator();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckDivisionIsNotAllowed,
                TypeScript.Net.Types.SyntaxKind.BinaryExpression);
        }

          private static void CheckDivisionIsNotAllowed(INode node, DiagnosticContext context)
        {
            var binaryExpression = node.As<BinaryExpression>();
            if (binaryExpression.OperatorToken.Kind == TypeScript.Net.Types.SyntaxKind.SlashToken)
            {
                context.Logger.ReportDivisionOperatorIsNotSupported(context.LoggingContext, node.LocationForLogging(context.SourceFile));
            }
        }
    }
}
