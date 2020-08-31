using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestProject
{
    [TestClass]
    public class CreationAnalyzerTests
    {
        [TestMethod]
        public async Task EmptyMethodGeneratesNoDiagnostics()
        {
            var code = @"
public static class Program
{
    public static void Main()
    {
    }
}";
            ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(code);

            Assert.AreEqual(0, diagnostics.Length);
        }

        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyPropertyGeneratesOneDiagnostic()
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
            ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(code);

            Assert.AreEqual(1, diagnostics.Length);

            var diagnostic = diagnostics[0];

            Assert.AreEqual(diagnostic.Id, "BadWayOfCreatingImmutableArray");

            var location = diagnostic.Location;

            var lineSpan = location.GetLineSpan();

            Assert.AreEqual(7, lineSpan.StartLinePosition.Line);
        }


        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyPropertyWithoutOpeningTheImmutableNamespaceGeneratesOneDiagnostic()
        {
            var code = @"
public static class Program
{
    public static void Main()
    {
        var array = System.Collections.Immutable.ImmutableArray<int>.Empty.Add(1);
    }
}";
            ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(code);

            Assert.AreEqual(1, diagnostics.Length);

            var diagnostic = diagnostics[0];

            Assert.AreEqual(diagnostic.Id, "BadWayOfCreatingImmutableArray");

            var location = diagnostic.Location;

            var lineSpan = location.GetLineSpan();

            Assert.AreEqual(5, lineSpan.StartLinePosition.Line);
        }

        [TestMethod]
        public async Task CreatingAnImmutableArrayViaTheEmptyPropertyUsingTheAliasDirectiveFeatureToCreateAnAliasForImmutableArrayOfIntGeneratesOneDiagnostic()
        {
            var code = @"
using imOfInt = System.Collections.Immutable.ImmutableArray<int>;

public static class Program
{
    public static void Main()
    {
        var array = imOfInt.Empty.Add(1);
    }
}";
            ImmutableArray<Diagnostic> diagnostics = await GetDiagnostics(code);

            Assert.AreEqual(1, diagnostics.Length);

            var diagnostic = diagnostics[0];

            Assert.AreEqual(diagnostic.Id, "BadWayOfCreatingImmutableArray");

            var location = diagnostic.Location;

            var lineSpan = location.GetLineSpan();

            Assert.AreEqual(7, lineSpan.StartLinePosition.Line);
        }

        public static Task<ImmutableArray<Diagnostic>> GetDiagnostics(string code)
        {
            return Utilities.GetDiagnostics(code);
        }
    }
}
