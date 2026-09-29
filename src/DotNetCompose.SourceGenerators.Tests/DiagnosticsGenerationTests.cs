using Microsoft.CodeAnalysis.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotNetCompose.SourceGenerators.Tests;

public sealed class DiagnosticsGenerationTests
{
    private const string Source = """
        using DotNetCompose.Runtime;

        namespace Integration;

        public static partial class Example
        {
            [Composable]
            public static void Greeting(int count)
            {
                _ = count;
            }
        }
        """;

    [Fact]
    public void DiagnosticsAreGeneratedByDefault()
    {
        string generated = GeneratorTestHelper.RunSingleGenerator(Source);

        Assert.Contains("CompositionDiagnosticsRuntime.Begin", generated);
        Assert.Contains("CompositionDiagnosticsRuntime.End", generated);
        Assert.Contains("CompositionDiagnosticsToken", generated);
        Assert.Contains("ComposableExecutionOutcome", generated);
        Assert.Contains("stackalloc byte[] { __count_state }", generated);
        Assert.DoesNotContain("catch", generated);
        Assert.DoesNotContain("CompositionDiagnosticsFlags", generated);
    }

    [Fact]
    public void DiagnosticsCanBeExplicitlyEnabled()
    {
        string generated = GeneratorTestHelper.RunSingleGenerator(
            Source,
            generateDiagnostics: true);

        Assert.Contains("CompositionDiagnosticsRuntime.Begin", generated);
        Assert.Contains("CompositionDiagnosticsRuntime.End", generated);
        Assert.DoesNotContain(
            GeneratorTestHelper.GetOutputCompilationDiagnostics(Source, generateDiagnostics: true),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DiagnosticsCanBeRemovedAtGenerationTime()
    {
        string generated = GeneratorTestHelper.RunSingleGenerator(
            Source,
            generateDiagnostics: false);

        Assert.DoesNotContain("CompositionDiagnosticsRuntime", generated);
        Assert.DoesNotContain("CompositionDiagnosticsToken", generated);
        Assert.Contains("EndRestartableGroup(", generated);
        Assert.DoesNotContain(
            GeneratorTestHelper.GetOutputCompilationDiagnostics(Source, generateDiagnostics: false),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EarlyReturnFlowsThroughDiagnosticsEndButLambdaReturnIsUntouched()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace Integration;

            public static partial class Example
            {
                [Composable]
                public static void Child()
                {
                }

                [Composable]
                public static void Greeting(int count)
                {
                    Func<int> nested = () => { return count; };
                    if (count == 0)
                    {
                        Child();
                        return;
                    }
                    _ = nested();
                }
            }
            """;

        IReadOnlyList<(string HintName, string Source)> results =
            GeneratorTestHelper.RunGenerator(source);
        Assert.True(results.Count == 1,
            string.Join(Environment.NewLine, GeneratorTestHelper.GetDiagnostics(source)));
        string generated = results[0].Source;

        Assert.Contains("goto __dncDiagnosticsEnd;", generated);
        Assert.Contains("return count;", generated);
        Assert.Contains("__dncDiagnosticsEnd:", generated);
        Assert.Contains("CompositionDiagnosticsRuntime.End", generated);
        Assert.Matches(@"EndReplaceableGroup\([^)]*\);\s*goto __dncDiagnosticsEnd;", generated);
        Assert.DoesNotContain(
            GeneratorTestHelper.GetOutputCompilationDiagnostics(source),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SourceMappingIsIndependentFromDiagnosticsAndSupportsSpaces()
    {
        const string path = @"C:\project with spaces\Greeting.cs";
        string generated = GeneratorTestHelper.RunSingleGenerator(
            Source,
            generateDiagnostics: false,
            path: path);

        string[] lines = generated.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int directiveIndex = Array.FindIndex(lines,
            line => line.Contains("#line (10, 9) - (10, 19)", StringComparison.Ordinal));
        Assert.True(directiveIndex >= 0, generated);
        string mappedLine = lines[directiveIndex + 1];
        int expectedOffset = mappedLine.TakeWhile(char.IsWhiteSpace).Count();
        Assert.Equal(
            $"#line (10, 9) - (10, 19) {expectedOffset} \"C:\\\\project with spaces\\\\Greeting.cs\"",
            lines[directiveIndex].TrimStart());
        Assert.Contains("#line hidden", generated);
        Assert.DoesNotContain("CompositionDiagnosticsRuntime", generated);
    }

    [Fact]
    public void LegacyLanguageVersionUsesLineOnlyDirective()
    {
        const string path = @"C:\project with spaces\Greeting.cs";
        string generated = GeneratorTestHelper.RunSingleGenerator(
            Source,
            langVersion: LanguageVersion.CSharp9,
            generateDiagnostics: false,
            path: path);

        Assert.Contains("#line 10 \"C:\\\\project with spaces\\\\Greeting.cs\"", generated);
        Assert.DoesNotContain("#line (", generated);
    }

    [Fact]
    public void ComplexComposableExpressionGetsHiddenCallAndSourceAnchor()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace Integration;

            public static partial class Example
            {
                [Composable]
                public static void Child() { }

                [Composable]
                public static void Host([Composable] Action content) { content(); }

                [Composable]
                public static void Screen()
                {
                    Host(() =>
                    {
                        Child();
                    });
                }
            }
            """;
        (int hostLine, int hostColumn) = FindSourcePosition(source, "Host(() =>");

        string generated = GeneratorTestHelper.RunSingleGenerator(
            source,
            generateDiagnostics: false,
            path: @"C:\project with spaces\Anchor.cs");

        string anchorDirective = $"#line ({hostLine}, {hostColumn})";
        const string generatedCall = "global::Integration.Example.Builders.Host";
        int directiveIndex = generated.IndexOf(anchorDirective, StringComparison.Ordinal);
        Assert.True(directiveIndex >= 0, generated);

        int anchorOpenIndex = generated.IndexOf('{', directiveIndex);
        Assert.True(anchorOpenIndex > directiveIndex, generated);

        int hiddenIndex = generated.IndexOf("#line hidden", anchorOpenIndex, StringComparison.Ordinal);
        Assert.True(hiddenIndex > anchorOpenIndex, generated);

        int anchorCloseIndex = generated.IndexOf('}', hiddenIndex);
        Assert.True(anchorCloseIndex > hiddenIndex, generated);

        int callHiddenIndex = generated.IndexOf("#line hidden", anchorCloseIndex, StringComparison.Ordinal);
        Assert.True(callHiddenIndex > anchorCloseIndex, generated);

        int generatedCallIndex = generated.IndexOf(generatedCall, callHiddenIndex, StringComparison.Ordinal);
        Assert.True(generatedCallIndex > callHiddenIndex, generated);

        Assert.DoesNotContain(
            GeneratorTestHelper.GetOutputCompilationDiagnostics(
                source,
                generateDiagnostics: false),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void SimpleExpressionDoesNotGetSourceAnchor()
    {
        string generated = GeneratorTestHelper.RunSingleGenerator(
            Source,
            generateDiagnostics: false);

        Assert.DoesNotContain("{ }", generated);
    }

    [Fact]
    public void ControlFlowStatementsRetainSourceLocationsAfterRewrite()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace Integration;

            public static partial class Example
            {
                [Composable]
                public static void Child() { }

                [Composable]
                public static void Screen(int count, int[] values)
                {
                    if (count > 0)
                    {
                        Child();
                    }

                    for (int index = 0; index < count; index++)
                    {
                        Child();
                    }

                    foreach (int value in values)
                    {
                        Child();
                    }
                }
            }
            """;
        (int ifLine, int ifColumn) = FindSourcePosition(source, "if (count > 0)");
        (int forLine, int forColumn) = FindSourcePosition(source, "for (int index");
        (int foreachLine, int foreachColumn) = FindSourcePosition(source, "foreach (int value");

        string generated = GeneratorTestHelper.RunSingleGenerator(
            source,
            generateDiagnostics: false,
            path: @"C:\project with spaces\ControlFlow.cs");

        Assert.Contains($"#line ({ifLine}, {ifColumn})", generated);
        Assert.Contains($"#line ({forLine}, {forColumn})", generated);
        Assert.Contains($"#line ({foreachLine}, {foreachColumn})", generated);
        Assert.DoesNotContain(
            GeneratorTestHelper.GetOutputCompilationDiagnostics(
                source,
                generateDiagnostics: false),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void PortablePdbMapsOriginalAndGeneratedMethodToSameUserSpan()
    {
        const string path = @"C:\project with spaces\Greeting.cs";
        var (compilation, driver) = GeneratorTestHelper.CreateDriver(
            Source,
            generateDiagnostics: false,
            path: path);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        using MemoryStream pe = new MemoryStream();
        using MemoryStream pdb = new MemoryStream();
        EmitResult result = output.Emit(
            pe,
            pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        pe.Position = 0;
        pdb.Position = 0;
        using PEReader peReader = new PEReader(pe, PEStreamOptions.LeaveOpen);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb);
        MetadataReader assemblyReader = peReader.GetMetadataReader();
        MetadataReader pdbReader = provider.GetMetadataReader();
        Assert.Contains(pdbReader.Documents,
            handle => pdbReader.GetString(pdbReader.GetDocument(handle).Name) == path);

        SequencePoint[] original = GetSequencePoints(
            assemblyReader,
            pdbReader,
            "Integration.Example",
            "Greeting");
        SequencePoint[] generated = GetSequencePoints(
            assemblyReader,
            pdbReader,
            "Integration.Example+Builders",
            "Greeting");

        Assert.Contains(original, IsGreetingUserSpan);
        Assert.Contains(generated, IsGreetingUserSpan);
        Assert.DoesNotContain(generated,
            point => !point.IsHidden && point.StartLine == 10 && point.StartColumn != 9);
        Assert.Contains(generated, point => point.IsHidden);
        Assert.NotEmpty(pdbReader.LocalScopes);
    }

    [Fact]
    public void PortablePdbMapsNestedComposableLambdaToExactSourceColumn()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace Integration;

            public static partial class Example
            {
                [Composable]
                public static void Child(string text) { }

                [Composable]
                public static void Container([Composable] Action content) { content(); }

                [Composable]
                public static void Screen(int count)
                {
                    int visible = count;
                    Container(() =>
                    {
                        if (visible > 0)
                            Child("nested");
                    });
                }
            }
            """;
        const string path = @"C:\project with spaces\Nested.cs";
        (int localLine, int localColumn) = FindSourcePosition(source, "int visible = count;");
        (int containerLine, int containerColumn) = FindSourcePosition(source, "Container(() =>");
        (int ifLine, int ifColumn) = FindSourcePosition(source, "if (visible > 0)");
        (int nestedLine, int nestedColumn) = FindSourcePosition(source, "Child(\"nested\");");
        var (compilation, driver) = GeneratorTestHelper.CreateDriver(
            source,
            generateDiagnostics: false,
            path: path);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        using MemoryStream pe = new MemoryStream();
        using MemoryStream pdb = new MemoryStream();
        EmitResult result = output.Emit(
            pe,
            pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        pe.Position = 0;
        pdb.Position = 0;
        using PEReader peReader = new PEReader(pe, PEStreamOptions.LeaveOpen);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb);
        MetadataReader assemblyReader = peReader.GetMetadataReader();
        MetadataReader pdbReader = provider.GetMetadataReader();

        SequencePoint[] screen = GetSequencePoints(
            assemblyReader,
            pdbReader,
            "Integration.Example+Builders",
            "Screen");
        Assert.Contains(screen,
            point => IsVisibleAt(point, localLine, localColumn));
        SequencePoint anchorPoint = Assert.Single(
            screen,
            point => !point.IsHidden && point.StartLine == containerLine);
        Assert.Equal(containerColumn, anchorPoint.StartColumn);
        Assert.Equal(containerLine, anchorPoint.EndLine);
        Assert.DoesNotContain(screen,
            point => IsVisibleAt(point, containerLine, containerColumn + 2));

        SequencePoint[] generatedMethods = GetSequencePoints(
            assemblyReader,
            pdbReader,
            typeNamePrefix: "Integration.Example+Builders",
            methodName: null);
        Assert.Contains(generatedMethods,
            point => IsVisibleAt(point, nestedLine, nestedColumn));
        Assert.Contains(generatedMethods,
            point => IsVisibleAt(point, ifLine, ifColumn));
    }

    private static bool IsGreetingUserSpan(SequencePoint point) =>
        !point.IsHidden &&
        point.StartLine == 10 && point.StartColumn == 9 &&
        point.EndLine == 10 && point.EndColumn == 19;

    private static bool IsVisibleAt(SequencePoint point, int line, int column) =>
        !point.IsHidden && point.StartLine == line && point.StartColumn == column;

    private static (int Line, int Column) FindSourcePosition(string source, string text)
    {
        int position = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(position >= 0, $"Could not find '{text}' in test source.");
        string before = source.Substring(0, position);
        int line = before.Count(character => character == '\n') + 1;
        int lastNewLine = before.LastIndexOf('\n');
        int column = position - lastNewLine;
        return (line, column);
    }

    private static SequencePoint[] GetSequencePoints(
        MetadataReader assemblyReader,
        MetadataReader pdbReader,
        string typeNamePrefix,
        string? methodName)
    {
        var points = new List<SequencePoint>();
        foreach (MethodDefinitionHandle methodHandle in assemblyReader.MethodDefinitions)
        {
            MethodDefinition method = assemblyReader.GetMethodDefinition(methodHandle);
            if (methodName != null && assemblyReader.GetString(method.Name) != methodName)
                continue;

            string typeName = GetTypeName(assemblyReader, method.GetDeclaringType());
            bool typeMatches = methodName == null
                ? typeName.StartsWith(typeNamePrefix, StringComparison.Ordinal)
                : typeName == typeNamePrefix;
            if (!typeMatches)
                continue;

            int row = MetadataTokens.GetRowNumber(methodHandle);
            MethodDebugInformationHandle debugHandle = MetadataTokens.MethodDebugInformationHandle(row);
            points.AddRange(pdbReader.GetMethodDebugInformation(debugHandle).GetSequencePoints());
        }

        Assert.NotEmpty(points);
        return points.ToArray();
    }

    private static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaringType = type.GetDeclaringType();
        if (!declaringType.IsNil)
            return GetTypeName(reader, declaringType) + "+" + name;

        string typeNamespace = reader.GetString(type.Namespace);
        return string.IsNullOrEmpty(typeNamespace) ? name : typeNamespace + "." + name;
    }
}
