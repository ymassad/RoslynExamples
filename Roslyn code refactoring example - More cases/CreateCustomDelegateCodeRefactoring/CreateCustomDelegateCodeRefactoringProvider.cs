﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace CreateCustomDelegateCodeRefactoring
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(CreateCustomDelegateCodeRefactoringProvider))]
    [Shared]
    public class CreateCustomDelegateCodeRefactoringProvider : CodeRefactoringProvider
    {
        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var document = context.Document;

            var root = await document.GetSyntaxRootAsync();

            if (!(root.FindNode(context.Span) is ParameterSyntax parameterSyntax))
                return;


            var semanticModel = await document.GetSemanticModelAsync();

            if (!(semanticModel.GetDeclaredSymbol(parameterSyntax) is IParameterSymbol parameterSymbol))
                return;

            var funcOrAction = TryGetFuncOrAction(parameterSymbol.Type, parameterSyntax);

            if (funcOrAction is null)
                return;

            context.RegisterRefactoring(CodeAction.Create(
                "Create Custom Delegate", async c =>
                {
                    return await UpdateSolutionToAddAndUseANewDelegate(
                        context.Document,
                        funcOrAction,
                        parameterSyntax,
                        parameterSymbol,
                        semanticModel);
                }));
        }

        private async Task<Solution> UpdateSolutionToAddAndUseANewDelegate(
            Document document,
            FuncOrAction funcOrAction,
            ParameterSyntax parameterSyntax,
            IParameterSymbol parameterSymbol,
            SemanticModel semanticModel)
        {
            var allChanges = new Dictionary<DocumentId, ChangesForDocument>();

            void AddChange(DocumentId documentId, Change change)
            {
                if(allChanges.TryGetValue(documentId, out var changes))
                {
                    allChanges[documentId] = changes.Add(change);
                }
                else
                {
                    allChanges[documentId] = new ChangesForDocument(documentId, ImmutableArray.Create(change));
                }
            }

            CreateDelegateDeclaration(
                document,
                funcOrAction,
                parameterSymbol,
                out var newDelegateName,
                out var delegateDeclaration);

            var method = (MethodDeclarationSyntax)parameterSyntax.Parent.Parent;

            var containingType = (TypeDeclarationSyntax)method.Parent;

            var indexOfMethodWithinSiblingMembers = containingType.Members.IndexOf(method);

            var changeToAddDelegate = new Change(containingType, x =>
            {
                var possibleChangedContainingType = (TypeDeclarationSyntax)x;

                var newMembers = possibleChangedContainingType.Members.Insert(
                    indexOfMethodWithinSiblingMembers,
                    delegateDeclaration);

                return possibleChangedContainingType
                    .WithMembers(SyntaxFactory.List(newMembers))
                    .NormalizeWhitespace();
            });

            AddChange(document.Id, changeToAddDelegate);

            var annotationForParameter = new SyntaxAnnotation();

            var changeToParameter = new Change(parameterSyntax, x =>
            {
                var potentiallyChangedParameter = (ParameterSyntax)x;

                return potentiallyChangedParameter
                    .WithType(
                        SyntaxFactory.IdentifierName(newDelegateName));
            });

            AddChange(document.Id, changeToParameter);

            var containingTypeSymbol = semanticModel.GetDeclaredSymbol(containingType);

            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var qualifiedDelegateType = SyntaxFactory.QualifiedName(
                (NameSyntax)syntaxGenerator.TypeExpression(containingTypeSymbol),
                SyntaxFactory.IdentifierName(newDelegateName));

            var parametersThatAreSourcesOfParameter = await FindSourceParameters(
                parameterSymbol,
                document.Project.Solution);

            foreach (var callerParameterAndDocument in parametersThatAreSourcesOfParameter)
            {
                var callerDocument = callerParameterAndDocument.document;

                var callerDocumentRoot = await callerDocument.GetSyntaxRootAsync();

                var callerParameterSyntax = (ParameterSyntax)callerDocumentRoot.FindNode(
                    callerParameterAndDocument.parameter.Locations.Single().SourceSpan);

                var newParameterType = callerDocument.Id == document.Id
                    ? (TypeSyntax) SyntaxFactory.IdentifierName(newDelegateName)
                    : qualifiedDelegateType;

                var change = new Change(callerParameterSyntax, x =>
                {
                    var potentiallyChangedParameterSyntax = (ParameterSyntax)x;
                    return potentiallyChangedParameterSyntax.WithType(newParameterType);
                });

                AddChange(callerDocument.Id, change);
            }

            var changes = allChanges.Values.ToImmutableArray();

            return await ApplyChanges(document.Project.Solution, changes);
        }

        private async Task<ImmutableArray<(IParameterSymbol parameter, Document document)>> FindSourceParameters(
            IParameterSymbol parameterSymbol, Solution solution)
        {
            var containingMethod = (IMethodSymbol)parameterSymbol.ContainingSymbol;

            var referencesToMethod = await SymbolFinder.FindCallersAsync(containingMethod, solution);

            var invocations = referencesToMethod
                .SelectMany(x => x.Locations)
                .Select(x =>
                {
                    var document = solution.GetDocument(x.SourceTree);

                    var root = x.SourceTree.GetRoot();

                    var node = root.FindNode(x.SourceSpan);

                    InvocationExpressionSyntax invocation;

                    if (node.Parent is InvocationExpressionSyntax inv)
                    {
                        invocation = inv;
                    }
                    else
                    {
                        invocation = (InvocationExpressionSyntax)node.Parent.Parent;
                    }

                    return (invocation, document);
                })
                .ToImmutableArray();

            var result = new List<(IParameterSymbol parameter, Document document)>();

            foreach (var invocationAndDocument in invocations)
            {
                var semanticModel = await invocationAndDocument.document.GetSemanticModelAsync();

                var operation = semanticModel.GetOperation(invocationAndDocument.invocation);

                if (!(operation is IInvocationOperation invocationOperation))
                    continue;

                var argument = (ArgumentSyntax) invocationOperation.Arguments
                    .First(a => a.Parameter.Ordinal == parameterSymbol.Ordinal).Syntax;

                if (semanticModel.GetSymbolInfo(argument.Expression).Symbol is IParameterSymbol parameter)
                {
                    result.Add((parameter, invocationAndDocument.document));
                }
            }

            return result.ToImmutableArray();
        }

        private void CreateDelegateDeclaration(Document document, FuncOrAction funcOrAction, IParameterSymbol parameterSymbol, out string newDelegateName, out DelegateDeclarationSyntax delegateDeclaration)
        {
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var parameters = funcOrAction switch
            {
                FuncOrAction.Action action => action.Parameters,
                FuncOrAction.Func func => func.Parameters,
                _ => throw new System.NotImplementedException()
            };

            newDelegateName = MakeFirstLetterUpperCase(parameterSymbol.Name);
            delegateDeclaration = (DelegateDeclarationSyntax)syntaxGenerator.DelegateDeclaration(
                newDelegateName,
                parameters
                    .Select(p => syntaxGenerator.ParameterDeclaration(
                        p.Name,
                        syntaxGenerator.TypeExpression(p.Type))),
                accessibility: Accessibility.Public);
            if (funcOrAction is FuncOrAction.Func func1)
            {
                delegateDeclaration = delegateDeclaration
                    .WithReturnType((TypeSyntax)syntaxGenerator.TypeExpression(func1.ReturnType));
            }
        }

        private string MakeFirstLetterUpperCase(string name)
        {
            return char.ToUpper(name[0]) + name.Substring(1);
        }

        public FuncOrAction? TryGetFuncOrAction(ITypeSymbol type, ParameterSyntax parameterSyntax)
        {
            if (!(type is INamedTypeSymbol delegateTypeSymbol))
                return null;

            if (!(parameterSyntax.Type is GenericNameSyntax delegateType))
                return null;

            Parameter CreateParameterFromTypeArgument(int typeArgumentIndex)
            {
                var typeArgumentSyntax = delegateType.TypeArgumentList.Arguments[typeArgumentIndex];

                var nameFromMultilineComment = TryGetNameFromMultilineComment(typeArgumentSyntax);

                var parameterInInvokeMethod =
                    delegateTypeSymbol.DelegateInvokeMethod.Parameters[typeArgumentIndex];

                var parmeterName = nameFromMultilineComment ??
                    parameterInInvokeMethod.Name;

                var parameterType = parameterInInvokeMethod.Type;

                return new Parameter(parmeterName, parameterType);
            }

            if (delegateTypeSymbol.TypeKind != TypeKind.Delegate)
                return null;

            var fullName = GetFullname(delegateTypeSymbol);

            if (fullName == "System.Action")
            {
                //Action<string>

                var parameters = delegateTypeSymbol.TypeArguments
                    .Select((x, i) => CreateParameterFromTypeArgument(i))
                    .ToImmutableArray();

                return new FuncOrAction.Action(parameters);
            }
            else if (fullName == "System.Func")
            {
                //Func<string /*p1*/, int>

                var parameters = delegateTypeSymbol.TypeArguments.Take(delegateTypeSymbol.TypeArguments.Length - 1)
                    .Select((x, i) => CreateParameterFromTypeArgument(i))
                    .ToImmutableArray();

                return new FuncOrAction.Func(parameters, delegateTypeSymbol.TypeArguments.Last());
            }

            return null;
        }

        private static string? TryGetNameFromMultilineComment(TypeSyntax typeArgumentSyntax)
        {
            var allTrivia = typeArgumentSyntax.GetTrailingTrivia().Where(z => z.Kind() == SyntaxKind.MultiLineCommentTrivia)
                .ToList();

            if (allTrivia.Count != 1)
            {
                return null;
            }

            var trivia = allTrivia.First();

            return GetContentOfMultilineCommentTrivia(trivia);
        }

        private static string GetContentOfMultilineCommentTrivia(SyntaxTrivia trivia)
        {
            return trivia.ToFullString().Replace("/*", "").Replace("*/", "");
        }

        private static string GetFullname(INamedTypeSymbol type)
        {
            if (type.ContainingType is INamedTypeSymbol containingType)
            {
                return GetFullname(containingType) + "." + type.Name;
            }

            if (type.ContainingNamespace.IsGlobalNamespace)
            {
                return type.Name;
            }

            return GetFullname(type.ContainingNamespace) + "." + type.Name;
        }

        private static string GetFullname(INamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace)
                return "";

            if (ns.ContainingNamespace.IsGlobalNamespace)
                return ns.Name;

            return GetFullname(ns.ContainingNamespace) + "." + ns.Name;
        }

        public static async Task<Solution> ApplyChanges(Solution solution, ImmutableArray<ChangesForDocument> changesForDocuments)
        {
            foreach(var changeForDocument in changesForDocuments)
            {
                var document = solution.GetDocument(changeForDocument.DocumentId);

                var rootNode = await document.GetSyntaxRootAsync();

                var changes = changeForDocument.Changes;

                var updatedRootNode = rootNode.ReplaceNodes(
                    changes.Select(x => x.Node),
                    (orgNode, potentiallyChangedNode) =>
                {
                    var change = changes.First(x => ReferenceEquals(x.Node, orgNode));

                    var updatedNode = change.UpdateSyntaxNode(potentiallyChangedNode);

                    return updatedNode;
                });

                solution = solution.WithDocumentSyntaxRoot(changeForDocument.DocumentId, updatedRootNode);
            }

            return solution;

        }
    }

    public delegate SyntaxNode UpdateSyntaxNode(SyntaxNode potentiallyChangedNode);

    public sealed class Change
    {
        public SyntaxNode Node { get; }

        public UpdateSyntaxNode UpdateSyntaxNode { get; }

        public Change(SyntaxNode node, UpdateSyntaxNode updateSyntaxNode)
        {
            Node = node;
            UpdateSyntaxNode = updateSyntaxNode;
        }
    }


    public sealed class ChangesForDocument
    {
        public ChangesForDocument(DocumentId documentId, ImmutableArray<Change> changes)
        {
            DocumentId = documentId;
            Changes = changes;
        }

        public DocumentId DocumentId { get; }

        public ImmutableArray<Change> Changes { get; }

        public ChangesForDocument Add(Change change)
        {
            return new ChangesForDocument(DocumentId, Changes.Add(change));
        }
    }
}
