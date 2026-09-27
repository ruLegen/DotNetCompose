using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Text;

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
            catch
            {
            }
        }

        try
        {
            refPaths.Add(typeof(object).Assembly.Location);
        }
        catch
        {
        }
        try
        {
            refPaths.Add(Assembly.Load("System.Runtime").Location);
        }
        catch
        {
        }
        try
        {
            refPaths.Add(typeof(System.Collections.Generic.List<>).Assembly.Location);
        }
        catch
        {
        }
        try
        {
            refPaths.Add(typeof(System.Linq.Enumerable).Assembly.Location);
        }
        catch
        {
        }

        References = refPaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    public static string LoadSource(string fileName)
        => File.ReadAllText(Path.Combine(TestSourcesDir, fileName));

    public static (Compilation Compilation, GeneratorDriver Driver) CreateDriver(
        string source,
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null,
        string path = "")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8),
            CSharpParseOptions.Default.WithLanguageVersion(langVersion),
            path: path);

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CreateGeneratorDriver(langVersion, generateDiagnostics);

        return (compilation, driver);
    }

    public static GeneratorDriver CreateGeneratorDriver(
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null)
    {
        var generator = new ComposeSourceGenerator();
        AnalyzerConfigOptionsProvider? optionsProvider = generateDiagnostics.HasValue
            ? new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
            {
                ["build_property.DotNetComposeGenerateDiagnostics"] = generateDiagnostics.Value ? "true" : "false"
            })
            : null;
        return CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(langVersion),
            optionsProvider: optionsProvider);
    }

    public static IReadOnlyList<(string HintName, string Source)> RunGenerator(
        string source,
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null,
        string path = "")
    {
        var (compilation, driver) = CreateDriver(source, langVersion, generateDiagnostics, path);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => (s.HintName, s.SourceText.ToString()))
            .ToArray();
    }

    public static string RunSingleGenerator(
        string source,
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null,
        string path = "")
    {
        var results = RunGenerator(source, langVersion, generateDiagnostics, path);
        Assert.Single(results);
        return results[0].Source;
    }

    public static ImmutableArray<Diagnostic> GetDiagnostics(
        string source,
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null)
    {
        var (compilation, driver) = CreateDriver(source, langVersion, generateDiagnostics);
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var result in runResult.Results)
        {
            builder.AddRange(result.Diagnostics);
        }
        return builder.ToImmutable();
    }

    public static ImmutableArray<Diagnostic> GetOutputCompilationDiagnostics(
        string source,
        LanguageVersion langVersion = LanguageVersion.Latest,
        bool? generateDiagnostics = null)
    {
        var (compilation, driver) = CreateDriver(source, langVersion, generateDiagnostics);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return output.GetDiagnostics();
    }

    public static Task RunSnapshotTest(string sourceFileName, string? verifyFileName = null)
    {
        if (string.IsNullOrEmpty(verifyFileName))
            verifyFileName = sourceFileName;

        string inputFileName = sourceFileName + ".cs";
        string outputFileName = verifyFileName + ".g";
        string source = LoadSource(inputFileName);
        string result = RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName(outputFileName);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty =
            new DictionaryAnalyzerConfigOptions(new Dictionary<string, string>());

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> values) =>
            GlobalOptions = new DictionaryAnalyzerConfigOptions(values);

        public override AnalyzerConfigOptions GlobalOptions { get; }
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }

    private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public DictionaryAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) =>
            _values = values;

        public override bool TryGetValue(string key, out string value) =>
            _values.TryGetValue(key, out value!);
    }
}
