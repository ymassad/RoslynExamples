using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyzerProject
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class CreationAnalyzer : DiagnosticAnalyzer
    {
        private static DiagnosticDescriptor DiagnosticDescriptor =
            new DiagnosticDescriptor(
                "BadWayOfCreatingImmutableArray",
                "Bad way of creating immutable array",
                "Bad way of creating immutable array",
                "Immutable arrays",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor>
            SupportedDiagnostics => ImmutableArray.Create(DiagnosticDescriptor);

        //using im = System.Collections.Immutable;
        //ImmutableArray<int>.Empty.Add(1);
        //im.ImmutableArray<int>.Empty.Add(1);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(
                Analyze,
                SyntaxKind.InvocationExpression);

        }

        private void Analyze(SyntaxNodeAnalysisContext context)
        {
            var node = (InvocationExpressionSyntax) context.Node;

            if (node.ArgumentList.Arguments.Count != 1)
                return;

            if (!(node.Expression is MemberAccessExpressionSyntax addAccess))
                return;

            if (addAccess.Name.Identifier.Text != "Add")
                return;

            if (!(addAccess.Expression is MemberAccessExpressionSyntax emptyAccess))
                return;

            if (emptyAccess.Name.Identifier.Text != "Empty")
                return;

            if (!(context.SemanticModel.GetSymbolInfo(emptyAccess.Expression).Symbol
                is INamedTypeSymbol imSymbol))
                return;

            if (imSymbol.Name != "ImmutableArray")
                return;

            if (imSymbol.TypeArguments.Length != 1)
                return;

            var fullnameOfNamespace = GetFullname(imSymbol.ContainingNamespace);

            if (fullnameOfNamespace != "System.Collections.Immutable")
                return;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptor,
                    node.GetLocation()));
        }

        private static string GetFullname(INamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace)
                return "";

            if (ns.ContainingNamespace.IsGlobalNamespace)
                return ns.Name;

            return GetFullname(ns.ContainingNamespace) + "." + ns.Name;
        }
    }
}
