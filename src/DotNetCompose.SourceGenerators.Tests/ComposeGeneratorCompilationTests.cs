namespace DotNetCompose.SourceGenerators.Tests;

public class ComposeGeneratorCompilationTests
{
    [Fact]
    public void GeneratedCodeCompilesWithoutErrors()
    {
        var source = GeneratorTestHelper.LoadSource("EmptyComposable.cs");

        var diags = GeneratorTestHelper.GetDiagnostics(source);

        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void GeneratedCodeContainsBuilderClass()
    {
        var source = GeneratorTestHelper.LoadSource("EmptyComposable.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);

        Assert.Contains("Builders", result);
    }

    [Fact]
    public void GeneratedCodeContainsContextParameters()
    {
        var source = GeneratorTestHelper.LoadSource("EmptyComposable.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);

        Assert.Contains("__ctx", result);
        Assert.Contains("IComposerContext", result);
    }

    [Fact]
    public void RestartCaptureUsesUniqueLocalsAndStackAllocatedStates()
    {
        string parameters = string.Join(", ", Enumerable.Range(0, 40).Select(index => $"int value{index}"));
        string source = $$"""
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class ManyParameters
            {
                [Composable]
                public static void Content({{parameters}})
                {
                    byte __dncRestartChanged0 = 0;
                    int __dncScopeUpdater = __dncRestartChanged0;
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetDiagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("__dncRestartChanged0_1", generated);
        Assert.Contains("ComposableArgumentsState.Forced(stackalloc byte[]", generated);
        Assert.DoesNotContain("CloneValues()", generated);
        Assert.DoesNotContain("new byte[]", generated);
    }

    [Fact]
    public void MultipleComposableMethodsInOneClass_SingleGeneratedFile()
    {
        var source = GeneratorTestHelper.LoadSource("MultipleComposableMethods.cs");
        var results = GeneratorTestHelper.RunGenerator(source);

        Assert.Single(results);
        var code = results[0].Source;
        Assert.Contains("A", code);
        Assert.Contains("B", code);
        Assert.Contains("C", code);
    }

    [Fact]
    public void NoComposableMethods_NoGeneratedOutput()
    {
        var source = GeneratorTestHelper.LoadSource("NoComposableMethods.cs");
        var results = GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(results);
    }

    [Fact]
    public void IgnoredMethods_NotInGeneratedOutput()
    {
        var source = GeneratorTestHelper.LoadSource("IgnoredComposable.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);

        Assert.Contains("NotIgnored", result);
        Assert.DoesNotContain("void Ignored(", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposableInAnotherClass_SeparateGeneratedFiles()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableInAnotherClass.cs");
        var results = GeneratorTestHelper.RunGenerator(source);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Source.Contains("MethodA"));
        Assert.Contains(results, r => r.Source.Contains("MethodB"));
    }

    [Fact]
    public void FullTestClass_GeneratesWithoutCompilationErrors()
    {
        var source = GeneratorTestHelper.LoadSource("FullTestClass.cs");

        var diags = GeneratorTestHelper.GetDiagnostics(source);

        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FullTestClass_HasBuilderForEachComposableMethod()
    {
        var source = GeneratorTestHelper.LoadSource("FullTestClass.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);

        Assert.Contains("EmptyComposable", result);
        Assert.Contains("Unstable", result);
        Assert.Contains("Stable", result);
        Assert.Contains("ComposableTest", result);
    }

    [Fact]
    public void ComposableWithDefault_GeneratesWithoutCompilationErrors()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithDefault.cs");
        var diags = GeneratorTestHelper.GetDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComposableWithDefault_ContainsDefaultParamState()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithDefault.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        Assert.Contains("__defaultParamState", result);
        Assert.Contains("ComposableArgumentsDefaultState", result);
    }

    [Fact]
    public void ComposableWithDefault_ContainsDefaultSubstitution()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithDefault.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        Assert.Contains("MyIntProvider.Value", result);
        Assert.Contains("ShouldUseDefault", result);
    }


    [Fact]
    public void InstanceComposable_ReportsDedicatedDiagnostic()
    {
        var source = GeneratorTestHelper.LoadSource("NotStaticClass.cs");
        var diagnostics = GeneratorTestHelper.GetDiagnostics(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DNC011");
    }

    [Theory]
    [InlineData("async", "DNC012")]
    [InlineData("iterator", "DNC013")]
    [InlineData("byref", "DNC014")]
    [InlineData("direct", "DNC015")]
    public void UnsupportedSubset_ReportsDedicatedDiagnostic(string scenario, string expectedId)
    {
        string member = scenario switch
        {
            "async" => "[Composable] public static async System.Threading.Tasks.Task Content() { await System.Threading.Tasks.Task.Yield(); }",
            "iterator" => "[Composable] public static System.Collections.Generic.IEnumerable<int> Content() { yield return 1; }",
            "byref" => "[Composable] public static void Content(ref int value) { }",
            _ => "[Composable] public static void Content() { } public static void Caller() { Content(); }"
        };
        string source = $$"""
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static partial class Unsupported { {{member}} }
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == expectedId);
    }

    [Fact]
    public void CompositionLocalProvider_IsTransformedAndCompiles()
    {
        string source = GeneratorTestHelper.LoadSource("CompositionLocalProvider.cs");
        var (compilation, _) = GeneratorTestHelper.CreateDriver(source);
        string result = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetDiagnostics(source);

        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        int redirectedCalls = result.Split("Composables.Builders.CompositionLocalProvider").Length - 1;
        Assert.True(redirectedCalls == 2, result);
    }

}
