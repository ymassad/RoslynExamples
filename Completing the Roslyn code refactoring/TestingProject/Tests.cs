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
    public class Tests
    {
        [TestMethod]
        public async Task BasicTest()
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

            string actualUpdatedText = await ApplyCodeRefactoring(code);

            Assert.AreEqual(expectedUpdatedCode, actualUpdatedText);
        }

        [TestMethod]
        public async Task TestThatWeHandleACallerInAnotherDocument()
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

            var callerCode = @"
using System;
public static class CallerClass1
{
    public static void Method2(Action<string /*firstName*/> write)
    {
        Class1.Method1(write);
    }
}
";

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

            var expectedCallerCode = @"
using System;
public static class CallerClass1
{
    public static void Method2(Class1.Write write)
    {
        Class1.Method1(write);
    }
}
";

            var (actualUpdatedText, actualUpdatedCallerDocumentText) = await ApplyCodeRefactoring(code, callerCode);

            Assert.AreEqual(expectedUpdatedCode, actualUpdatedText);
            Assert.AreEqual(expectedCallerCode, actualUpdatedCallerDocumentText);
        }

        [TestMethod]
        public async Task TestThatWeHandleACallerInAnotherDocumentAndACallerInTheSameDocument()
        {
            var code = @"
using System;
public static class Class1
{
    public static void Method1(Action<string /*firstName*/> write)
    {
        write(""Adam"");
    }

    public static void CallerInMyDocument(Action<string /*firstName*/> write)
    {
        Method1(write);
    }
}";

            var callerCode = @"
using System;
public static class CallerClass1
{
    public static void Method2(Action<string /*firstName*/> write)
    {
        Class1.Method1(write);
    }
}
";

            var expectedUpdatedCode = @"
using System;
public static class Class1
{
    public delegate void Write(string firstName);
    public static void Method1(Write write)
    {
        write(""Adam"");
    }

    public static void CallerInMyDocument(Write write)
    {
        Method1(write);
    }
}";

            var expectedCallerCode = @"
using System;
public static class CallerClass1
{
    public static void Method2(Class1.Write write)
    {
        Class1.Method1(write);
    }
}
";

            var (actualUpdatedText, actualUpdatedCallerDocumentText) = await ApplyCodeRefactoring(code, callerCode);

            Assert.AreEqual(expectedUpdatedCode, actualUpdatedText);
            Assert.AreEqual(expectedCallerCode, actualUpdatedCallerDocumentText);
        }


        private static async Task<string> ApplyCodeRefactoring(
            string code)
        {
            var result = await ApplyCodeRefactoring(code, "");

            return result.updatedTextForDocument1;
        }

        private static async Task<(string updatedTextForDocument1, string updatedTextForDocument2)> ApplyCodeRefactoring(
            string code, string codeInSecondDocument)
        {
            var workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = solution.AddProject(projectId, "Project1", "Project1", LanguageNames.CSharp);

            var documentId = DocumentId.CreateNewId(projectId);

            solution = solution.AddDocument(documentId,
                "Document.cs",
                code);

            var document2Id = DocumentId.CreateNewId(projectId);

            solution = solution.AddDocument(document2Id,
                "Document2.cs",
                codeInSecondDocument);

            var project = solution.GetProject(projectId)
                .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            if (!workspace.TryApplyChanges(project.Solution))
                throw new Exception("Unable to apply changes");

            project = workspace.CurrentSolution.GetProject(projectId);

            var sut = new CreateCustomDelegateCodeRefactoringProvider();

            var document = project.GetDocument(documentId);

            var root = await document.GetSyntaxRootAsync();

            var parameter = root.DescendantNodes().OfType<ParameterSyntax>().First();

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

            foreach (var operation in operations)
            {
                operation.Apply(workspace, CancellationToken.None);
            }

            var updatedDocument = workspace.CurrentSolution.GetDocument(documentId);

            var actualUpdatedText = (await updatedDocument.GetTextAsync()).ToString();

            var updatedDocument2 = workspace.CurrentSolution.GetDocument(document2Id);

            var actualUpdatedTextForDocument2 = (await updatedDocument2.GetTextAsync()).ToString();
            
            return (actualUpdatedText, actualUpdatedTextForDocument2);
        }
    }
}
