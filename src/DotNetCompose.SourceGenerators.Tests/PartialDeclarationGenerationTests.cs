using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace DotNetCompose.SourceGenerators.Tests;

public sealed class PartialDeclarationGenerationTests
{
    private const string Counter = """
        using DotNetCompose.Runtime;
        using DotNetCompose.Runtime.Snapshots;
        namespace PartialImports;
        public static partial class Example
        {
            [Composable(ComposableMode.Inline)]
            public static SnapshotMutableState<int> Counter(int initial)
            {
                return Composables.RememberState(initial);
            }
        }
        """;

    private const string Effects = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotNetCompose.Runtime;
        namespace PartialImports;
        public static partial class Example
        {
            [Composable(ComposableMode.Inline)]
            public static void Effect(Func<CancellationToken, ValueTask> block)
            {
                Composables.LaunchedEffect(null, block);
            }
        }
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PartialSignaturesKeepTheirOwnImports(bool reverse, bool diagnostics)
    {
        var sources = new[] { ("Counter.cs", Counter), ("Effects.cs", Effects) };
        var input = CreateCompilation(reverse ? sources.Reverse().ToArray() : sources);
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, input);

        Assert.Equal(2, result.Sources.Count);
        string counter = result.Sources.Values.Single(source => source.Contains(" Counter("));
        string effects = result.Sources.Values.Single(source => source.Contains(" Effect("));
        Assert.Contains("using DotNetCompose.Runtime.Snapshots;", counter);
        Assert.DoesNotContain("using System.Threading;", counter);
        Assert.Contains("using System.Threading;", effects);
        Assert.DoesNotContain("using DotNetCompose.Runtime.Snapshots;", effects);
        var builders = result.Output.GetTypeByMetadataName("PartialImports.Example+Builders")!;
        var returnType = ((IMethodSymbol)Assert.Single(builders.GetMembers("Counter"))).ReturnType;
        Assert.Equal("SnapshotMutableState", returnType.Name);
        Assert.Equal(TypeKind.Class, returnType.TypeKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AliasesAndStaticImportsRemainIndependent(bool diagnostics)
    {
        const string support = """
            namespace Models
            {
                public class Left { public int Value; }
                public class Right { public int Value; }
            }
            namespace Constants
            {
                public static class Left { public static int Value => 11; }
                public static class Right { public static int Value => 22; }
            }
            """;
        string Part(string side) => $$"""
            using DotNetCompose.Runtime;
            using Model = Models.{{side}};
            using static Constants.{{side}};
            namespace PartialImports;
            public static partial class Example
            {
                [Composable(ComposableMode.Inline)]
                public static Model {{side}}()
                {
                    return new Model { Value = Value };
                }
            }
            """;
        const string entry = """
            namespace PartialImports;
            public static partial class Example
            {
                public static int Execute()
                {
                    return Builders.Left(null!, default, default).Value * 100
                        + Builders.Right(null!, default, default).Value;
                }
            }
            """;
        var input = CreateCompilation(("Left.cs", Part("Left")), ("Right.cs", Part("Right")),
            ("Support.cs", support), ("Entry.cs", entry));
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, input);
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(1122, Execute(result.Output, "PartialImports.Example"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceScopesKeepRelativeImportsAndExcludeSiblingImports(bool diagnostics)
    {
        const string source = """
            using DotNetCompose.Runtime;
            using RootModel = RootModels.Model;
            namespace Outer.Models { public class Model { public int Value = 17; } }
            namespace RootModels { public class Model { public int Value = 3; } }
            namespace Outer
            {
                using Model = Models.Model;
                namespace Inner
                {
                    using static System.Math;
                    public static partial class Example
                    {
                        [Composable(ComposableMode.Inline)]
                        public static Model Create()
                        {
                            RootModel root = new RootModel();
                            return new Model { Value = Abs(-root.Value) + 17 };
                        }
                        public static int Execute() => Builders.Create(null!, default, default).Value;
                    }
                }
            }
            namespace Sibling
            {
                using Model = RootModels.Model;
            }
            """;
        var input = CreateCompilation(("Namespaces.cs", source));
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, input);
        string generated = Assert.Single(result.Sources).Value;
        Assert.Contains("using Model = Models.Model;", generated);
        Assert.DoesNotContain("namespace Sibling", generated);
        Assert.DoesNotContain("using Model = RootModels.Model;", generated);
        Assert.Equal(20, Execute(result.Output, "Outer.Inner.Example"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalAliasesAreAvailableWithoutDuplicatingDirectives(bool diagnostics)
    {
        const string globals = """
            global using Model = GlobalModels.Model;
            global using static System.Math;
            namespace GlobalModels { public class Model { public int Value; } }
            """;
        const string source = """
            using DotNetCompose.Runtime;
            public static partial class Example
            {
                [Composable(ComposableMode.Inline)]
                public static Model Create() { return new Model { Value = Abs(-23) }; }
                public static int Execute() => Builders.Create(null!, default, default).Value;
            }
            """;
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, CreateCompilation(("Globals.cs", globals), ("Example.cs", source)));
        Assert.DoesNotContain("global using", Assert.Single(result.Sources).Value);
        Assert.Equal(23, Execute(result.Output, "Example"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleDeclarationsInOneFilePreserveDistinctNamespaceScopes(bool diagnostics)
    {
        const string source = """
            using DotNetCompose.Runtime;
            namespace Constants
            {
                public static class Left { public static int Value => 7; }
                public static class Right { public static int Value => 9; }
            }
            namespace PartialImports
            {
                using static Constants.Left;
                public static partial class Example
                {
                    [Composable(ComposableMode.Inline)]
                    public static int Left() { return Value; }
                    public static int Execute() => Builders.Left(null!, default, default) * 10
                        + Builders.Right(null!, default, default);
                }
            }
            namespace PartialImports
            {
                using static Constants.Right;
                public static partial class Example
                {
                    [Composable(ComposableMode.Inline)]
                    public static int Right() { return Value; }
                }
            }
            """;
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, CreateCompilation(("Parts.cs", source)));
        Assert.Equal(2, result.Sources.Count);
        Assert.Equal(79, Execute(result.Output, "PartialImports.Example"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultProviderAliasesStayWithTheirPartialDeclaration(bool diagnostics)
    {
        const string support = """
            using DotNetCompose.Runtime;
            namespace Providers
            {
                public class Left : IDefaultValueProvider { public static int Create() => 10; }
                public class Right : IDefaultValueProvider { public static int Create() => 20; }
            }
            """;
        string Part(string side) => $$"""
            using DotNetCompose.Runtime;
            using Provider = Providers.{{side}};
            namespace PartialImports;
            public static partial class Example
            {
                [Composable]
                public static void {{side}}([Default<Provider>] int value = default) { _ = value; }
            }
            """;
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, CreateCompilation(("Left.cs", Part("Left")),
            ("Right.cs", Part("Right")), ("Providers.cs", support)));
        foreach (string side in new[] { "Left", "Right" })
        {
            string generated = result.Sources.Values.Single(source => source.Contains($"void {side}("));
            Assert.Contains($"using Provider = Providers.{side};", generated);
            Assert.Contains($"global::Providers.{side}.Create()", generated);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredLambdasAndGenericConstraintsWorkAcrossPartialParts(bool diagnostics)
    {
        string Part(string side, int value, bool helpers) => $$"""
            using System;
            using DotNetCompose.Runtime;
            using Base = System.IDisposable;
            namespace PartialImports;
            public partial class Example<T> where T : Base
            {
                [Composable(ComposableMode.Inline)]
                public void {{side}}() { Sink(() => Result += {{value}}); }
                [Composable(ComposableMode.Inline)]
                public static void Static{{side}}() { StaticSink(() => Result += {{value}}); }
                {{(helpers ? """
                public static int Result;
                [Composable(ComposableMode.Inline)]
                private void Sink([Composable] Action block) { block(); }
                [Composable(ComposableMode.Inline)]
                private static void StaticSink([Composable] Action block) { block(); }
                public static int Execute()
                {
                    var instance = new Example<T>();
                    instance.Left(null!, default, default);
                    instance.Right(null!, default, default);
                    Builders.StaticLeft(null!, default, default);
                    Builders.StaticRight(null!, default, default);
                    return Result;
                }
                """ : "")}}
            }
            """;
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var result = Run(ref driver, CreateCompilation(("Left.cs", Part("Left", 1, true)),
            ("Right.cs", Part("Right", 2, false))));
        Assert.Equal(2, result.Sources.Count);
        Assert.All(result.Sources.Values, source => Assert.Contains("static partial class", source));
        Assembly assembly = Emit(result.Output);
        Type type = assembly.GetType("PartialImports.Example`1")!.MakeGenericType(typeof(MemoryStream));
        Assert.Equal(6, type.GetMethod("Execute")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequentialEditsUseCurrentScopesAndStableHintNames(bool diagnostics)
    {
        var input = CreateCompilation(("Counter.cs", Counter), ("Effects.cs", Effects));
        var driver = GeneratorTestHelper.CreateGeneratorDriver(generateDiagnostics: diagnostics);
        var first = Run(ref driver, input);
        var reversed = input.RemoveAllSyntaxTrees().AddSyntaxTrees(input.SyntaxTrees.Reverse());
        var second = Run(ref driver, reversed);
        Assert.Equal(first.Sources, second.Sources);

        string aliased = Counter.Replace("SnapshotMutableState<int>", "State")
            .Replace("using DotNetCompose.Runtime.Snapshots;", "using State = DotNetCompose.Runtime.Snapshots.SnapshotMutableState<int>;");
        foreach (string next in new[]
        {
            "// Comment\n" + Counter,
            Counter.Replace("RememberState(initial)", "RememberState(initial + 1)"),
            Counter.Replace("int", "long"),
            aliased,
            aliased.Replace("int", "long"),
            Counter
        })
        {
            // Always replace an input tree; generated trees never enter the next compilation.
            var previous = input.SyntaxTrees.Single(tree => tree.FilePath == "Counter.cs");
            input = input.ReplaceSyntaxTree(previous, Parse("Counter.cs", next));
            var result = Run(ref driver, input);
            Assert.Equal(first.Sources.Keys, result.Sources.Keys);
            string generated = result.Sources.Values.Single(source => source.Contains(" Counter("));
            Assert.Contains(next.Contains("initial + 1") ? "initial + 1" : "RememberState", generated);
            var method = (IMethodSymbol)Assert.Single(result.Output.GetTypeByMetadataName("PartialImports.Example+Builders")!.GetMembers("Counter"));
            Assert.Equal(next.Contains("long") ? SpecialType.System_Int64 : SpecialType.System_Int32,
                method.Parameters[0].Type.SpecialType);
        }
        Assert.Equal(first.Sources, Run(ref driver, input).Sources);
    }

    [Fact]
    public void RuntimeComposablesGenerateWhenReusablePartComesFirst()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../DotNetCompose.Runtime"));
        var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols("NET9_0_OR_GREATER");
        var trees = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .OrderBy(path => Path.GetFileName(path) == "Composables.Reusable.cs" ? 0 : 1)
            .ThenBy(path => path, StringComparer.Ordinal)
            .Select(path => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path), Encoding.UTF8), parseOptions, path))
            .ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("DotNetCompose.", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));
        var input = CSharpCompilation.Create("RuntimeImportRegression", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var driver = GeneratorTestHelper.CreateGeneratorDriver();
        var first = Run(ref driver, input);
        input = input.RemoveAllSyntaxTrees().AddSyntaxTrees(trees.Reverse());
        Assert.Equal(first.Sources, Run(ref driver, input).Sources);
    }

    [Fact]
    public void InferredGenericArgumentsAreFullyQualified()
    {
        const string provider = """
            using DotNetCompose.Runtime;
            namespace Foreign
            {
                public class Model { }
                public static class Factory { public static Model Create() => new Model(); }
                public static partial class Helpers
                {
                    [Composable(ComposableMode.Inline)]
                    public static void Consume<T>(T value) { }
                }
            }
            """;
        const string consumer = """
            using DotNetCompose.Runtime;
            namespace PartialImports;
            public static partial class Example
            {
                [Composable(ComposableMode.Inline)]
                public static void Content()
                {
                    var model = Foreign.Factory.Create();
                    Foreign.Helpers.Consume(model);
                }
            }
            """;
        var driver = GeneratorTestHelper.CreateGeneratorDriver();
        var result = Run(ref driver, CreateCompilation(("Provider.cs", provider), ("Consumer.cs", consumer)));
        Assert.Contains("Consume<global::Foreign.Model>", result.Sources.Values.Single(source => source.Contains(" Content(")));
    }

    private static SyntaxTree Parse(string path, string source)
        => CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: path);

    private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ComposableAttribute).Assembly.Location).Distinct();
        return CSharpCompilation.Create("PartialImports_" + Guid.NewGuid().ToString("N"),
            sources.Select(source => Parse(source.Path, source.Source)),
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static (Compilation Output, SortedDictionary<string, string> Sources) Run(
        ref GeneratorDriver driver, Compilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out var diagnostics);
        var run = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(run.Exception);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS8785" || diagnostic.Severity == DiagnosticSeverity.Error);
        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
        Assert.NotEmpty(run.GeneratedSources);
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in run.GeneratedSources)
            sources.Add(source.HintName, source.SourceText.ToString());
        return (output, sources);
    }

    private static Assembly Emit(Compilation output)
    {
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static int Execute(Compilation output, string typeName)
        => (int)Emit(output).GetType(typeName)!.GetMethod("Execute")!.Invoke(null, null)!;
}
