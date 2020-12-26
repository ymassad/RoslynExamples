using System.Linq;
using CreateCustomDelegateCodeRefactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CodeActions;
using System.Threading;
using System;

namespace TestingProject
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public async Task TestMethod1()
        {
            var code = @"
using System;

public static class Class1
{
    public static void Method1(Action<string /*firstName*/> write)
    {
        write(""Adam"");
    }
}";
            var expectedUpdatedCode = @"
using System;

public static class Class1
{
    public delegate void Write(string firstName);

    public static void Method1(Write write)
    {
        write(""Adam"");
    }
}";

            var workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = solution.AddProject(projectId, "Project1", "Project1", LanguageNames.CSharp);

            var documentId = DocumentId.CreateNewId(projectId);

            solution = solution.AddDocument(documentId,
                "Document.cs",
                code);

            var project = solution.GetProject(projectId)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            if (!workspace.TryApplyChanges(project.Solution))
                throw new Exception("Unable to apply changes");

            project = workspace.CurrentSolution.GetProject(projectId);

            var sut = new CreateCustomDelegateCodeRefactoringProvider();

            var document = project.GetDocument(documentId);

            var root = await document.GetSyntaxRootAsync();

            var parameter = root.DescendantNodes().OfType<ParameterSyntax>().Single();

            var registeredCodeActions = new List<CodeAction>();

            await sut.ComputeRefactoringsAsync(
                new CodeRefactoringContext(
                    document,
                    parameter.Identifier.Span,
                    codeAction =>
                    {
                        registeredCodeActions.Add(codeAction);
                    },
                    CancellationToken.None));

            if (registeredCodeActions.Count == 0)
                throw new Exception("No code actions registered");

            if (registeredCodeActions.Count > 1)
                throw new Exception("More than one action registered");

            var codeAction = registeredCodeActions[0];

            var operations = await codeAction.GetOperationsAsync(CancellationToken.None);

            foreach(var operation in operations)
            {
                operation.Apply(workspace, CancellationToken.None);
            }

            var updatedDocument = workspace.CurrentSolution.GetDocument(documentId);

            var actualUpdatedText = (await updatedDocument.GetTextAsync()).ToString();

            Assert.AreEqual(expectedUpdatedCode, actualUpdatedText);
        }
    }
}
