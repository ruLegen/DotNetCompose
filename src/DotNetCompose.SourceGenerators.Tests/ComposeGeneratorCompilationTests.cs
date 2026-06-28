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
    public void NonStaticClass_IsTransformed()
    {
        var source = GeneratorTestHelper.LoadSource("NotStaticClass.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        Assert.Contains("__ctx", result);
        Assert.Contains("IComposerContext", result);
    }
}
