using System.Text;

namespace DotNetCompose.SourceGenerators.Tests;

public sealed class IncrementalGenerationTests
{
    private const string Source = """
        using DotNetCompose.Runtime;

        namespace Incremental;

        public static partial class Example
        {
            [Composable]
            public static void Leaf(string value)
            {
                _ = value;
            }

            [Composable]
            public static void Content()
            {
                Leaf("before");
            }

            public static int Ordinary()
            {
                return 1;
            }
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReparsedIdenticalSourceUsesCurrentSyntaxTree(bool generateDiagnostics)
    {
        var (input, driver) = CreateDriver(generateDiagnostics);
        var first = RunAndAssert(ref driver, input);
        SyntaxTree originalTree = Assert.Single(input.SyntaxTrees);

        input = ReplaceTree(input, Source);

        Assert.NotSame(originalTree, Assert.Single(input.SyntaxTrees));
        var second = RunAndAssert(ref driver, input);
        Assert.Equal(first.Generated, second.Generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditedComposableWithUnchangedSiblingGeneratesLatestBody(bool generateDiagnostics)
    {
        var (input, driver) = CreateDriver(generateDiagnostics);
        RunAndAssert(ref driver, input);

        input = ReplaceTree(input, Source.Replace("\"before\"", "\"after\""));

        var updated = RunAndAssert(ref driver, input);
        Assert.Contains("\"after\"", updated.Generated);
        Assert.DoesNotContain("\"before\"", updated.Generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommentAndOrdinaryMethodEditsKeepGenerationWorking(bool generateDiagnostics)
    {
        var (input, driver) = CreateDriver(generateDiagnostics);
        RunAndAssert(ref driver, input);

        string movedSource = "// Source moved by one line\n" + Source;
        input = ReplaceTree(input, movedSource);
        var afterComment = RunAndAssert(ref driver, input);
        Assert.Contains("\"before\"", afterComment.Generated);

        input = ReplaceTree(input, movedSource.Replace("return 1;", "return 2;"));
        var afterOrdinaryEdit = RunAndAssert(ref driver, input);
        Assert.Equal(afterComment.Generated, afterOrdinaryEdit.Generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequentialBodyAndSignatureEditsAndReversionUseLatestDeclarations(bool generateDiagnostics)
    {
        var (input, driver) = CreateDriver(generateDiagnostics);
        var first = RunAndAssert(ref driver, input);
        AssertLeafParameter(first.Output, SpecialType.System_String, "value");

        string changedBody = Source.Replace("\"before\"", "\"after\"");
        input = ReplaceTree(input, changedBody);
        var afterBody = RunAndAssert(ref driver, input);
        Assert.Contains("\"after\"", afterBody.Generated);

        string changedType = changedBody.Replace("string value", "int value").Replace("\"after\"", "42");
        input = ReplaceTree(input, changedType);
        var afterType = RunAndAssert(ref driver, input);
        AssertLeafParameter(afterType.Output, SpecialType.System_Int32, "value");
        Assert.DoesNotContain("\"after\"", afterType.Generated);

        string changedName = changedType.Replace("int value", "int count").Replace("_ = value;", "_ = count;");
        input = ReplaceTree(input, changedName);
        var afterName = RunAndAssert(ref driver, input);
        AssertLeafParameter(afterName.Output, SpecialType.System_Int32, "count");

        input = ReplaceTree(input, Source);
        var reverted = RunAndAssert(ref driver, input);
        AssertLeafParameter(reverted.Output, SpecialType.System_String, "value");
        Assert.Equal(first.Generated, reverted.Generated);
    }

    private static (Compilation Input, GeneratorDriver Driver) CreateDriver(bool generateDiagnostics)
    {
        return GeneratorTestHelper.CreateDriver(Source, generateDiagnostics: generateDiagnostics, path: "Incremental.cs");
    }

    private static Compilation ReplaceTree(Compilation input, string source)
    {
        SyntaxTree previous = Assert.Single(input.SyntaxTrees);
        SyntaxTree next = CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)previous.Options,
            previous.FilePath, Encoding.UTF8);
        // Only the original input is updated; generated trees from prior passes must not be fed back.
        return input.ReplaceSyntaxTree(previous, next);
    }

    private static (Compilation Output, string Generated) RunAndAssert(ref GeneratorDriver driver, Compilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out var diagnostics);
        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS8785" || diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785" || diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(result.GeneratedSources);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (output, string.Join("\n", result.GeneratedSources.Select(source => source.SourceText.ToString())));
    }

    private static void AssertLeafParameter(Compilation output, SpecialType type, string name)
    {
        INamedTypeSymbol builders = output.GetTypeByMetadataName("Incremental.Example+Builders")!;
        Assert.NotNull(builders);
        IMethodSymbol leaf = Assert.Single(builders.GetMembers("Leaf").OfType<IMethodSymbol>());
        Assert.Equal(type, leaf.Parameters[0].Type.SpecialType);
        Assert.Equal(name, leaf.Parameters[0].Name);
    }
}
