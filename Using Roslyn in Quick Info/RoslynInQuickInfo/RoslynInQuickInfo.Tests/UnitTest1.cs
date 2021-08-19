using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RoslynInQuickInfo;

namespace QuickInfoUsingRoslyn.Tests
{
    [TestClass]
    public class Tests
    {
        [TestMethod]
        public async Task BasicTest()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        |Sha*pe| shape;
    }
}

public abstract class Shape
{
    private Shape()
    {

    }

    public sealed class Square : Shape
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }
}";


            var expectedMessage = @"Sum type cases:
Square";

            await RunTest(code, expectedMessage);
        }

        [TestMethod]
        public async Task Test_BaseTypeIsNotAbstract_NotConsideredSumType()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        Sha*pe shape;
    }
}

public class Shape
{
    private Shape()
    {

    }

    public sealed class Square : Shape
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }
}";

            await RunTest(code, null);
        }

        [TestMethod]
        public async Task Test_CaseIsGeneric_NotConsideredSumType()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        Sha*pe shape;
    }
}

public abstract class Shape
{
    private Shape()
    {

    }

    public sealed class Square<T> : Shape
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }
}";

            await RunTest(code, null);
        }

        [TestMethod]
        public async Task Test_ConstructorIsPublic_NotConsideredSumType()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        Sha*pe shape;
    }
}

public abstract class Shape
{
    public sealed class Square : Shape
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }
}";

            await RunTest(code, null);
        }

        [TestMethod]
        public async Task Test_CaseDoesNotInheritBaseType_NotConsideredSumType()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        Sha*pe shape;
    }
}

public abstract class Shape
{
    private Shape()
    {
    }

    public sealed class Square
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }
}";

            await RunTest(code, null);
        }

        [TestMethod]
        public async Task TwoCasesTest()
        {
            var code = @"
public static class Class1
{
    public static void Method1()
    {
        |Sha*pe| shape;
    }
}

public abstract class Shape
{
    private Shape()
    {

    }

    public sealed class Square : Shape
    {
        public int Length { get; }

        public Square(int length) => Length = length;
    }

    public sealed class Circle : Shape
    {
        public int Diameter { get; }

        public Circle(int diameter) => Diameter = diameter;
    }
}";


            var expectedMessage = @"Sum type cases:
Square
Circle";

            await RunTest(code, expectedMessage);
        }


        private static async Task RunTest(string code, string? expectedMessage)
        {
            TextSpan? GetExpectedSpan()
            {
                var code2 = code.Replace("*", "");

                var indexOfFirstBar = code2.IndexOf("|", StringComparison.InvariantCulture);

                if (indexOfFirstBar == -1)
                    return null;

                var indexOfSecondBar = code2.IndexOf("|", indexOfFirstBar + 1, StringComparison.InvariantCulture);

                if (indexOfSecondBar == -1)
                    return null;

                return TextSpan.FromBounds(indexOfFirstBar, indexOfSecondBar - 1);
            }

            var position = code.Replace("|", "").IndexOf("*");

            var expectedSpan = GetExpectedSpan();

            var workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = solution.AddProject(projectId, "Project1", "Project1", LanguageNames.CSharp);

            var project = solution.GetProject(projectId);

            project = project.AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            var document = project.AddDocument("File1.cs", code.Replace("|", "").Replace("*", ""));

            var result = await SumTypeQuickInfoSource.CalculateQuickInfo(
                document,
                position, CancellationToken.None);


            if (!expectedSpan.HasValue)
            {
                //We don't expect a quick info message

                if (result.HasValue)
                    throw new Exception("Unexpectedly, a quick info message was returned: " + result.Value.message);

                return;
            }

            if (expectedMessage is null)
                throw new Exception("Error in test, an expected message should be specified");

            if (!result.HasValue)
                throw new Exception("Unexpectedly, there was no quick info message returned");

            var (message, span) = result.Value;

            Assert.AreEqual(expectedSpan.Value, span);

            Assert.AreEqual(expectedMessage, message);
        }
    }
}
