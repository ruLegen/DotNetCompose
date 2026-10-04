using DotNetCompose.SourceGenerators.Helpers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection.Emit;
using System.Text;

namespace DotNetCompose.SourceGenerators.Tests;

public sealed class ArgumentStateBufferTests
{
    private const string Source = """
        using System;
        using DotNetCompose.Runtime;
        namespace Buffers;
        public sealed class ValueProvider : IDefaultValueProvider
        {
            public static int Create() { return 7; }
        }
        public static partial class Example
        {
            [Composable]
            public static void Leaf(int value, [Default<ValueProvider>] int secondary = default)
            {
                _ = value + secondary;
            }
            [Composable]
            public static void Invoke(int value, [Composable] Action<int> content)
            {
                content(value);
            }
            [Composable]
            public static void Content(int value)
            {
                Leaf(value);
                Invoke(value, item => { Leaf(item); Leaf(2); });
                Leaf(3);
            }
        }
        """;

    public static IEnumerable<object?[]> Modes()
    {
        foreach (OptimizationLevel level in Enum.GetValues<OptimizationLevel>())
        foreach (bool diagnostics in new[] { false, true })
        foreach (string? configured in new string?[] { null, "true", "false", "", "invalid" })
        {
            bool stackAlloc = bool.TryParse(configured, out bool explicitMode)
                ? explicitMode : level == OptimizationLevel.Release;
            yield return new object?[] { level, diagnostics, configured, stackAlloc };
        }
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void ConsumerOptionsSelectStorageAcrossAllGeneratedBuffers(
        OptimizationLevel level, bool diagnostics, string? configured, bool stackAlloc)
    {
        var (input, driver) = GeneratorTestHelper.CreateDriver(Source,
            generateDiagnostics: diagnostics, useStackAllocForArgumentStates: configured);
        input = input.WithOptions(input.Options.WithOptimizationLevel(level));
        var result = Run(ref driver, input);
        SyntaxNode root = result.Generated;

        SyntaxKind expected = stackAlloc ? SyntaxKind.StackAllocArrayCreationExpression : SyntaxKind.CollectionExpression;
        ExpressionSyntax[] buffers = GetBuffers(root);
        Assert.True(buffers.Length >= 7);
        Assert.All(buffers, buffer => Assert.Equal(expected, buffer.Kind()));
        Assert.Contains(root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>(),
            node => node.Type.ToString().EndsWith("ComposableArgumentsDefaultState"));
        Assert.Contains(root.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            node => node.Expression.ToString().EndsWith("ComposableArgumentsState.Forced"));
        Assert.Contains(root.DescendantNodes().OfType<InvocationExpressionSyntax>(),
            node => node.Expression.ToString() == "content");
        Assert.Equal(diagnostics, root.ToString().Contains("CompositionDiagnosticsRuntime.End"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderLanguageUsesArraysWithoutChangingArgumentStates(bool diagnostics)
    {
        var (input, driver) = GeneratorTestHelper.CreateDriver(Source, LanguageVersion.CSharp11,
            generateDiagnostics: diagnostics, useStackAllocForArgumentStates: "false");
        var result = Run(ref driver, input);
        Assert.All(GetBuffers(result.Generated), buffer => Assert.IsType<ArrayCreationExpressionSyntax>(buffer));

        driver = driver.WithUpdatedAnalyzerConfigOptions(GeneratorTestHelper.CreateOptionsProvider(diagnostics, "true"));
        string withStackAlloc = Run(ref driver, input).Generated.ToString();
        // The buffer contents and all surrounding generated behavior remain identical.
        Assert.Equal(withStackAlloc, result.Generated.ToString().Replace("new byte[]", "stackalloc byte[]"));
    }

    [Fact]
    public void UnsupportedRuntimeSelectsCompatibleArrays()
    {
        CSharpCompilation input = CSharpCompilation.Create("UnsupportedRuntime");
        Assert.False(input.SupportsRuntimeCapability(RuntimeCapability.InlineArrayTypes));
        Assert.Equal(ArgumentStateBufferStorage.Array, ArgumentStateBuffer.SelectStorage(
            input, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12), false));
    }

    [Fact]
    public void SettingsAndSourceEditsAreObservedByOneDriver()
    {
        var (input, driver) = GeneratorTestHelper.CreateDriver(Source, useStackAllocForArgumentStates: "true");
        var first = Run(ref driver, input);
        Assert.Contains(GetBuffers(first.Generated), node => node is StackAllocArrayCreationExpressionSyntax);

        driver = driver.WithUpdatedAnalyzerConfigOptions(GeneratorTestHelper.CreateOptionsProvider(
            generateDiagnostics: false, useStackAllocForArgumentStates: "false"));
        var changedSetting = Run(ref driver, input);
        Assert.All(GetBuffers(changedSetting.Generated), node => Assert.IsType<CollectionExpressionSyntax>(node));
        Assert.DoesNotContain("CompositionDiagnosticsRuntime", changedSetting.Generated.ToString());

        foreach (string source in new[]
        {
            "// Comment changed\n" + Source,
            Source.Replace("Leaf(3);", "Leaf(5);"),
            Source.Replace("Leaf(3);", "Leaf(8);")
        })
        {
            SyntaxTree previous = Assert.Single(input.SyntaxTrees);
            input = input.ReplaceSyntaxTree(previous, CSharpSyntaxTree.ParseText(source,
                (CSharpParseOptions)previous.Options, previous.FilePath, Encoding.UTF8));
            var updated = Run(ref driver, input);
            Assert.All(GetBuffers(updated.Generated), node => Assert.IsType<CollectionExpressionSyntax>(node));
            if (source.Contains("Leaf(8);"))
                Assert.Contains("Leaf(8,", updated.Generated.ToString());
        }

        driver = driver.WithUpdatedAnalyzerConfigOptions(GeneratorTestHelper.CreateOptionsProvider(
            generateDiagnostics: true, useStackAllocForArgumentStates: "true"));
        var revertedSetting = Run(ref driver, input);
        Assert.All(GetBuffers(revertedSetting.Generated), node => Assert.IsType<StackAllocArrayCreationExpressionSyntax>(node));
        Assert.Contains("Leaf(8,", revertedSetting.Generated.ToString());
    }

    [Fact]
    public void EmptyDiagnosticsBufferAndUserStackAllocArePreserved()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            namespace Buffers;
            public static partial class Example
            {
                [Composable]
                public static void Empty() { }
                [Composable]
                public static void UserBuffer()
                {
                    Span<byte> user = stackalloc byte[] { 1, 2 };
                    _ = user[0];
                }
            }
            """;
        var (input, driver) = GeneratorTestHelper.CreateDriver(source,
            useStackAllocForArgumentStates: "false");
        var result = Run(ref driver, input);
        Assert.Single(result.Generated.DescendantNodes().OfType<StackAllocArrayCreationExpressionSyntax>());
        Assert.Contains("ReadOnlySpan<byte>.Empty", result.Generated.ToString());
        Assert.DoesNotContain(result.Generated.DescendantNodes(), node => node is ArrayCreationExpressionSyntax);
    }

    [Theory]
    [InlineData(OptimizationLevel.Debug, LanguageVersion.CSharp12, false)]
    [InlineData(OptimizationLevel.Release, LanguageVersion.CSharp12, false)]
    [InlineData(OptimizationLevel.Debug, LanguageVersion.CSharp11, true)]
    public void BufferExpressionsHaveExpectedILAndAllocations(
        OptimizationLevel level, LanguageVersion language, bool heap)
    {
        var (referenceInput, _) = GeneratorTestHelper.CreateDriver(Source, language);
        referenceInput = referenceInput.WithOptions(referenceInput.Options.WithOptimizationLevel(level));
        ArgumentStateBufferStorage storage = ArgumentStateBuffer.SelectStorage(
            referenceInput, (CSharpParseOptions)Assert.Single(referenceInput.SyntaxTrees).Options, false);
        string buffer = ArgumentStateBuffer.Create(
            new[] { SyntaxFactory.IdentifierName("first"), SyntaxFactory.IdentifierName("second") }, storage)
            .NormalizeWhitespace().ToFullString();
        string source = $$"""
            using System;
            using System.Runtime.CompilerServices;
            using DotNetCompose.Runtime;
            public static class Probe
            {
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Changed(byte first, byte second) => ReadChanged(new ComposableArgumentsState({{buffer}}));
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Defaults(byte first, byte second) => ReadDefaults(new ComposableArgumentsDefaultState({{buffer}}));
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Restart(byte first, byte second) => ReadChanged(ComposableArgumentsState.Forced({{buffer}}));
                [MethodImpl(MethodImplOptions.NoInlining)]
                public static int Diagnostics(byte first, byte second) => Read({{buffer}});
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReadChanged(ComposableArgumentsState states) => states[0] + states[1];
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int ReadDefaults(ComposableArgumentsDefaultState states) => states[0] + states[1];
                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Read(ReadOnlySpan<byte> states) => states[0] + states[1];
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create("AllocationProbe_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(language)) },
            referenceInput.References, (CSharpCompilationOptions)referenceInput.Options);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Type probe = Assembly.Load(stream.ToArray()).GetType("Probe")!;
        MethodInfo[] methods = new[] { "Changed", "Defaults", "Restart", "Diagnostics" }
            .Select(name => probe.GetMethod(name)!).ToArray();
        Func<byte, byte, int>[] calls = methods.Select(method => method.CreateDelegate<Func<byte, byte, int>>()).ToArray();
        foreach (MethodInfo method in methods)
        {
            OpCode[] opcodes = ReadOpcodes(method).ToArray();
            Assert.DoesNotContain(OpCodes.Localloc, opcodes);
            Assert.Equal(heap, opcodes.Contains(OpCodes.Newarr));
        }
        for (int iteration = 0; iteration < 10000; iteration++)
            foreach (var call in calls)
                call(1, 2);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int iteration = 0; iteration < 100000; iteration++)
            foreach (var call in calls)
                sum += call(1, 2);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(1200000, sum);
        if (heap)
            Assert.True(allocated > 0);
        else
            Assert.Equal(0, allocated);
    }

    private static ExpressionSyntax[] GetBuffers(SyntaxNode root) => root.DescendantNodes()
        .OfType<ExpressionSyntax>().Where(node => node is StackAllocArrayCreationExpressionSyntax
            or CollectionExpressionSyntax || node is ArrayCreationExpressionSyntax
            {
                Type.ElementType: PredefinedTypeSyntax element
            } && element.Keyword.IsKind(SyntaxKind.ByteKeyword)).ToArray();

    private static (Compilation Output, SyntaxNode Generated) Run(ref GeneratorDriver driver, Compilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out var diagnostics);
        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(diagnostics.Concat(result.Diagnostics),
            diagnostic => diagnostic.Id == "CS8785" || diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return (output, Assert.Single(result.GeneratedSources).SyntaxTree.GetRoot());
    }

    private static IEnumerable<OpCode> ReadOpcodes(MethodInfo method)
    {
        var lookup = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opcode => unchecked((ushort)opcode.Value));
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            ushort value = il[offset++];
            if (value == 0xfe)
                value = (ushort)(0xfe00 | il[offset++]);
            OpCode opcode = lookup[value];
            yield return opcode;
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
        }
    }
}
