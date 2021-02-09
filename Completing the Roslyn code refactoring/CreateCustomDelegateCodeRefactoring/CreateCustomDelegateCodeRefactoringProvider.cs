using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
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
            CreateDelegateDeclaration(
                document,
                funcOrAction,
                parameterSymbol,
                out var newDelegateName,
                out var delegateDeclaration);

            var method = (MethodDeclarationSyntax)parameterSyntax.Parent.Parent;

            var annotationForParameter = new SyntaxAnnotation();

            var updatedMethod = method.ReplaceNode(parameterSyntax,
                parameterSyntax
                    .WithType(
                        SyntaxFactory.IdentifierName(newDelegateName))
                    .WithAdditionalAnnotations(annotationForParameter));

            var root = await document.GetSyntaxRootAsync();

            var containingType = (TypeDeclarationSyntax)method.Parent;

            var containingTypeSymbol = semanticModel.GetDeclaredSymbol(containingType);

            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var qualifiedDelegateType = SyntaxFactory.QualifiedName(
                (NameSyntax)syntaxGenerator.TypeExpression(containingTypeSymbol),
                SyntaxFactory.IdentifierName(newDelegateName));

            var indexOfMethodWithinSiblingMembers = containingType.Members.IndexOf(method);

            var solution = document.Project.Solution;

            var updatedRoot = root.ReplaceNodes(new SyntaxNode[] { method, containingType },
                (originalNode, possiblyChangedNode) =>
                {
                    if (originalNode == method)
                    {
                        return updatedMethod;
                    }
                    else if (originalNode == containingType)
                    {
                        var possibleChangedContainingType = (TypeDeclarationSyntax)possiblyChangedNode;

                        var newMembers = possibleChangedContainingType.Members.Insert(
                            indexOfMethodWithinSiblingMembers,
                            delegateDeclaration);

                        return possibleChangedContainingType.WithMembers(SyntaxFactory.List(newMembers));
                    }

                    throw new System.Exception("Unexpected: originalNode is not any of the nodes passed to ReplaceNodes");
                });

            solution = solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);

            var newDocument = solution.GetDocument(document.Id);

            var newSemanticModel = await newDocument.GetSemanticModelAsync();

            var newDocumentRootNode = await newDocument.GetSyntaxRootAsync();

            var newParameterSyntax =
                (ParameterSyntax) newDocumentRootNode.GetAnnotatedNodes(annotationForParameter).Single();

            var newParameterSymbol = newSemanticModel.GetDeclaredSymbol(newParameterSyntax);

            var parametersThatAreSourcesOfParameter = await FindSourceParameters(
                newParameterSymbol,
                solution);

            foreach (var callerParameterAndDocument in parametersThatAreSourcesOfParameter)
            {
                var callerDocument = callerParameterAndDocument.document;

                var callerDocumentRoot = await callerDocument.GetSyntaxRootAsync();

                var callerParameterSyntax = (ParameterSyntax)callerDocumentRoot.FindNode(
                    callerParameterAndDocument.parameter.Locations.Single().SourceSpan);

                var newParameterType = callerDocument.Id == document.Id
                    ? (TypeSyntax) SyntaxFactory.IdentifierName(newDelegateName)
                    : qualifiedDelegateType;

                var updatedCallerParameterSyntax = callerParameterSyntax.WithType(newParameterType);

                var updatedCallerDocumentRoot = callerDocumentRoot
                    .ReplaceNode(callerParameterSyntax, updatedCallerParameterSyntax);

                solution = solution.WithDocumentSyntaxRoot(callerDocument.Id, updatedCallerDocumentRoot);
            }


            return solution;
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
                var argument = invocationAndDocument.invocation.ArgumentList.Arguments[parameterSymbol.Ordinal];

                var semanticModel = await invocationAndDocument.document.GetSemanticModelAsync();

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
    }
}
