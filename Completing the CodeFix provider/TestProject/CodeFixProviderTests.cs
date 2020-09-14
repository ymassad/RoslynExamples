using AnalyzerProject;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestProject
{
    [TestClass]
    public class CodeFixProviderTests
    {
        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyPropertyWithoutOpeningTheImmutableNamespace_CodeFixMakesTheCodeUseTheCreateMethod()
        {
            var code = @"
public static class Program
{
    public static void Main()
    {
        var array = System.Collections.Immutable.ImmutableArray<int>.Empty.Add(1);
    }
}";

            var expectedChangedCode = @"
public static class Program
{
    public static void Main()
    {
        var array = System.Collections.Immutable.ImmutableArray.Create(1);
    }
}";
            await RunTest(code, expectedChangedCode);
        }

        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyPropertyWithoutUsingFullNameOfImmutableArrayType_CodeFixMakesTheCodeUseTheCreateMethod()
        {
            var code = @"
using System.Collections.Immutable;

public static class Program
{
    public static void Main()
    {
        var array = ImmutableArray<int>.Empty.Add(1);
    }
}";

            var expectedChangedCode = @"
using System.Collections.Immutable;

public static class Program
{
    public static void Main()
    {
        var array = ImmutableArray.Create(1);
    }
}";
            await RunTest(code, expectedChangedCode);
        }

        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyProperty_ArrayTypeIsLongButValueTypeIsInt_CodeFixMakesTheCodeUseTheCreateMethod_CorrectArrayTypeIsCreated()
        {
            var code = @"
using System.Collections.Immutable;

public static class Program
{
    public static void Main()
    {
        var array = ImmutableArray<long>.Empty.Add(1);
    }
}";

            var expectedChangedCode = @"
using System.Collections.Immutable;

public static class Program
{
    public static void Main()
    {
        var array = ImmutableArray.Create<long>(1);
    }
}";
            await RunTest(code, expectedChangedCode);
        }


        private static async Task RunTest(string code, string expectedChangedCode)
        {
            var (diagnostics, document, workspace) = await Utilities.GetDiagnosticsAdvanced(code);

            Assert.AreEqual(1, diagnostics.Length);

            var diagnostic = diagnostics[0];

            var codeFixProvider = new CreationCodeFixProvider();

            CodeAction registeredCodeAction = null;

            var context = new CodeFixContext(document, diagnostic, (codeAction, _) =>
            {
                if (registeredCodeAction != null)
                    throw new Exception("Code action was registered more than once");

                registeredCodeAction = codeAction;

            }, CancellationToken.None);

            await codeFixProvider.RegisterCodeFixesAsync(context);

            if (registeredCodeAction == null)
                throw new Exception("Code action was not registered");

            var operations = await registeredCodeAction.GetOperationsAsync(CancellationToken.None);

            foreach (var operation in operations)
            {
                operation.Apply(workspace, CancellationToken.None);
            }

            var updatedDocument = workspace.CurrentSolution.GetDocument(document.Id);


            var newCode = (await updatedDocument.GetTextAsync()).ToString();

            Assert.AreEqual(expectedChangedCode, newCode);
        }
    }
}
