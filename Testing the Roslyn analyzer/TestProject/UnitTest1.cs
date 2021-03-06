﻿using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using AnalyzerProject;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Reflection;

namespace TestProject
{
    [TestClass]
    public class UnitTest1
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

        private static async Task<ImmutableArray<Diagnostic>> GetDiagnostics(string code)
        {
            AdhocWorkspace workspace = new AdhocWorkspace();

            var solution = workspace.CurrentSolution;

            var projectId = ProjectId.CreateNewId();

            solution = solution
                .AddProject(
                    projectId,
                    "MyTestProject",
                    "MyTestProject",
                    LanguageNames.CSharp);

            solution = solution
                .AddDocument(DocumentId.CreateNewId(projectId),
                "File.cs",
                code);

            var project = solution.GetProject(projectId);

            project = project.AddMetadataReference(
                MetadataReference.CreateFromFile(
                    typeof(object).Assembly.Location))
                .AddMetadataReferences(GetAllReferencesNeededForType(typeof(ImmutableArray)));

            var compilation = await project.GetCompilationAsync();

            var compilationWithAnalyzer = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(
                    new CreationAnalyzer()));

            var diagnostics = await compilationWithAnalyzer.GetAllDiagnosticsAsync();
            return diagnostics;
        }

        private static MetadataReference[] GetAllReferencesNeededForType(Type type)
        {
            var files = GetAllAssemblyFilesNeededForType(type);

            return files.Select(x => MetadataReference.CreateFromFile(x)).Cast<MetadataReference>().ToArray();
        }

        private static ImmutableArray<string> GetAllAssemblyFilesNeededForType(Type type)
        {
            return type.Assembly.GetReferencedAssemblies()
                .Select(x => Assembly.Load(x.FullName))
                .Append(type.Assembly)
                .Select(x => x.Location)
                .ToImmutableArray();
        }
    }
}
