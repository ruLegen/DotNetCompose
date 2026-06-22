namespace DotNetCompose.SourceGenerators.Tests;

public class ComposeGeneratorSnapshotTests
{
    static ComposeGeneratorSnapshotTests()
    {
        Verifier.DerivePathInfo((sourceFile, projectDirectory, type, method) =>
            new PathInfo(directory: Path.Combine(projectDirectory, "Snapshots")));
    }

    [Fact]
    public Task EmptyComposable()
    {
        var source = GeneratorTestHelper.LoadSource("EmptyComposable.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("EmptyComposable.g");
    }

    [Fact]
    public Task ComposableWithLambdas()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithLambdas.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("ComposableWithLambdas.g");
    }

    [Fact]
    public Task IgnoredComposable()
    {
        var source = GeneratorTestHelper.LoadSource("IgnoredComposable.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("IgnoredComposable.g");
    }

    [Fact]
    public Task ComposableWithGenerics()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithGenerics.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("ComposableWithGenerics.g");
    }

    [Fact]
    public Task ComposableLambdaParameter()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableLambdaParameter.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("ComposableLambdaParameter.g");
    }

    [Fact]
    public Task MultipleComposableMethods()
    {
        var source = GeneratorTestHelper.LoadSource("MultipleComposableMethods.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("MultipleComposableMethods.g");
    }

    [Fact]
    public Task ComposableWithStaticUsings()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithStaticUsings.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("ComposableWithStaticUsings.g");
    }

    [Fact]
    public Task ComposableInAnotherClass()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableInAnotherClass.cs");
        var results = GeneratorTestHelper.RunGenerator(source);
        Assert.Equal(2, results.Count);
        return Verifier.Verify(results).UseFileName("ComposableInAnotherClass.g");
    }

    [Fact]
    public Task AnotherClassFile()
    {
        var source = GeneratorTestHelper.LoadSource("AnotherClassFile.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("AnotherClassFile.g");
    }

    [Fact]
    public Task FullTestClass()
    {
        var source = GeneratorTestHelper.LoadSource("FullTestClass.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("FullTestClass.g");
    }

    [Fact]
    public Task ComposableWithDefault()
    {
        var source = GeneratorTestHelper.LoadSource("ComposableWithDefault.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("ComposableWithDefault.g");
    }

    [Fact]
    public Task NonStaticClassComposableTransformation()
    {
        var source = GeneratorTestHelper.LoadSource("NotStaticClass.cs");
        var result = GeneratorTestHelper.RunSingleGenerator(source);
        return Verifier.Verify(result).UseFileName("NotStaticClass.g");
    }
}
