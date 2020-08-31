using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Composition;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace AnalyzerProject
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CreationCodeFixProvider))]
    [Shared]
    public class CreationCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create("BadWayOfCreatingImmutableArray");

        //System.Collections.Immutable.ImmutableArray<int>.Empty.Add(1);
        //System.Collections.Immutable.ImmutableArray.Create(1);

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var diagnostic = context.Diagnostics.First();

            var document = context.Document;

            var root = await document.GetSyntaxRootAsync();

            var node = root.FindNode(diagnostic.Location.SourceSpan);

            if (!(node is InvocationExpressionSyntax addInvocation))
                throw new Exception("Expected node to be of type InvocationExpressionSyntax");

            context.RegisterCodeFix(CodeAction.Create("Use ImmutableArray.Create", async c =>
            {
                var createMethodAccessExpr = CreateMemberAccessExpression(
                    "System.Collections.Immutable.ImmutableArray.Create");

                var updatedRoot = root.ReplaceNode(addInvocation.Expression, createMethodAccessExpr);

                return document.WithSyntaxRoot(updatedRoot);
            }), diagnostic);
        }

        private static MemberAccessExpressionSyntax CreateMemberAccessExpression(string accessExpression)
        {
            var parts = accessExpression.Split('.');

            if (parts.Length < 2)
                throw new Exception("There should at least be two parts");

            var memberAccessExpression =
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(parts[0]),
                        SyntaxFactory.IdentifierName(parts[1]));


            for(int i = 2; i < parts.Length; i++)
            {
                memberAccessExpression =
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                            memberAccessExpression,
                            SyntaxFactory.IdentifierName(parts[i]));
            }

            return memberAccessExpression;

        }
    }
}
