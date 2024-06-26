// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Ambients.Transformers;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge.Rules
{
    internal sealed class EnforceAmbientAccessInConfig : LanguageRule
    {
        private static readonly Dictionary<string, HashSet<string>> s_blocklistLookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        static EnforceAmbientAccessInConfig()
        {
            s_blocklistLookup.Add(
                AmbientContext.ContextName,
                new HashSet<string>(AmbientContext.ConfigBlocklist, StringComparer.OrdinalIgnoreCase));

            s_blocklistLookup.Add(AmbientFile.FileName, AllMembers);

            s_blocklistLookup.Add(AmbientDirectory.Name, AllMembers);

            s_blocklistLookup.Add(AmbientTransformerOriginal.Name, AllMembers);
            s_blocklistLookup.Add(AmbientTransformerHack.Name, AllMembers);

            s_blocklistLookup.Add(AmbientContract.ContractName, AllMembers);
        }

        private static HashSet<string> AllMembers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private EnforceAmbientAccessInConfig()
        { }

        public static EnforceAmbientAccessInConfig CreateAndRegister(AnalysisContext context)
        {
            var result = new EnforceAmbientAccessInConfig();
            result.Initialize(context);
            return result;
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                this,
                CheckAmbientCall,
                TypeScript.Net.Types.SyntaxKind.PropertyAccessExpression);
        }

        public override RuleAnalysisScope AnalysisScope => RuleAnalysisScope.RootConfig;

        private static void CheckAmbientCall([NotNull]INode node, [NotNull]DiagnosticContext context)
        {
            // Semantic information is not available here, so validation must inspect literals
            // PropertyAccessExpression: x.y
            var propertyAccessExpression = node.Cast<IPropertyAccessExpression>();

            // Right-hand side of property access as an identifier: y
            var rhsName = propertyAccessExpression.Name?.As<IIdentifier>()?.Text;

            // Left-hand side of property access as an identifier: x
            var lhsName = propertyAccessExpression.Expression?.As<IIdentifier>()?.Text;

            if (lhsName != null && rhsName != null)
            {
                // Check against blocklist
                // if the methods list is empty, then all the members are prohibited from using.
                if (s_blocklistLookup.TryGetValue(lhsName, out var methods) && (methods.Count == 0 || methods.Contains(rhsName)))
                {
                    context.Logger.ReportAmbientAccessInConfig(
                        context.LoggingContext,
                        node.LocationForLogging(context.SourceFile),
                        lhsName,
                        rhsName);
                }
            }
        }
    }
}
