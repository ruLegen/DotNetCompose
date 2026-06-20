namespace DotNetCompose.SourceGenerators.Tests;

public static class GeneratorTestHelper
{
    private static readonly string TestSourcesDir;
    private static readonly MetadataReference[] References;

    static GeneratorTestHelper()
    {
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        TestSourcesDir = Path.Combine(asmDir, "TestSources");

        var refPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var runtimeAsm = typeof(ComposableAttribute).Assembly;
        refPaths.Add(runtimeAsm.Location);

        foreach (var refName in runtimeAsm.GetReferencedAssemblies())
        {
            try
            {
                var asm = Assembly.Load(refName);
                refPaths.Add(asm.Location);
            }
            catch { }
        }

        try { refPaths.Add(typeof(object).Assembly.Location); } catch { }
        try { refPaths.Add(Assembly.Load("System.Runtime").Location); } catch { }
        try { refPaths.Add(typeof(System.Collections.Generic.List<>).Assembly.Location); } catch { }
        try { refPaths.Add(typeof(System.Linq.Enumerable).Assembly.Location); } catch { }

        References = refPaths
            .Where(p => !string.IsNullOrEmpty(p) && !p.Contains("System.Private.CoreLib"))
            .Select(p => MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    public static string LoadSource(string fileName)
        => File.ReadAllText(Path.Combine(TestSourcesDir, fileName));

    public static (Compilation Compilation, GeneratorDriver Driver) CreateDriver(
        string source, LanguageVersion langVersion = LanguageVersion.Latest)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source,
            CSharpParseOptions.Default.WithLanguageVersion(langVersion));

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ComposeSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        return (compilation, driver);
    }

    public static IReadOnlyList<(string HintName, string Source)> RunGenerator(
        string source, LanguageVersion langVersion = LanguageVersion.Latest)
    {
        var (compilation, driver) = CreateDriver(source, langVersion);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => (s.HintName, s.SourceText.ToString()))
            .ToArray();
    }

    public static string RunSingleGenerator(
        string source, LanguageVersion langVersion = LanguageVersion.Latest)
    {
        var results = RunGenerator(source, langVersion);
        Assert.Single(results);
        return results[0].Source;
    }

    public static ImmutableArray<Diagnostic> GetDiagnostics(
        string source, LanguageVersion langVersion = LanguageVersion.Latest)
    {
        var (compilation, driver) = CreateDriver(source, langVersion);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var result in runResult.Results)
        {
            builder.AddRange(result.Diagnostics);
        }
        return builder.ToImmutable();
    }
}
