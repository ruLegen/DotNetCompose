namespace DotNetCompose.SourceGenerators.Tests;

public sealed class ComposableModeTests
{
    [Theory]
    [InlineData(nameof(ComposableMode.Restartable))]
    [InlineData(nameof(ComposableMode.ReadOnly))]
    [InlineData(nameof(ComposableMode.Inline))]
    [InlineData(nameof(ComposableMode.NonSkippable))]
    [InlineData(nameof(ComposableMode.NonRestartable))]
    [InlineData(nameof(ComposableMode.ExplicitGroups))]
    public void AllSupportedMethodModesAreAccepted(string mode)
    {
        string source = $$"""
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Valid
            {
                [Composable(ComposableMode.{{mode}})]
                public static void Content() { }
            }
            """;

        Assert.DoesNotContain(
            GeneratorTestHelper.GetDiagnostics(source),
            diagnostic => diagnostic.Id == "DNC020");
    }

    [Theory]
    [InlineData(nameof(ComposableMode.Restartable))]
    [InlineData(nameof(ComposableMode.ReadOnly))]
    [InlineData(nameof(ComposableMode.Inline))]
    [InlineData(nameof(ComposableMode.ReadOnlyInline))]
    public void AllSupportedParameterModesAreAccepted(string mode)
    {
        string source = $$"""
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Valid
            {
                [Composable]
                public static void Content(
                    [Composable(ComposableMode.{{mode}})] Action content) { }
            }
            """;

        Assert.DoesNotContain(
            GeneratorTestHelper.GetDiagnostics(source),
            diagnostic => diagnostic.Id == "DNC020");
    }

    [Fact]
    public void MethodModesGenerateTheirDeclaredCompositionProtocol()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class ChildHost
            {
                [Composable]
                public static void Child() { }
            }

            public static partial class RestartableExample
            {
                [Composable]
                public static void Content(int value) { }
            }

            public static partial class ReadOnlyExample
            {
                [Composable(ComposableMode.ReadOnly)]
                public static void Content(int value) { }
            }

            public static partial class InlineExample
            {
                [Composable(ComposableMode.Inline)]
                public static void Content(int value)
                {
                    if (value > 0)
                    {
                        ChildHost.Child();
                    }
                }
            }

            public static partial class NonSkippableExample
            {
                [Composable(ComposableMode.NonSkippable)]
                public static void Content(int value) { }
            }

            public static partial class NonRestartableExample
            {
                [Composable(ComposableMode.NonRestartable)]
                public static void Content(int value)
                {
                    if (value > 0)
                    {
                        ChildHost.Child();
                    }
                }
            }

            public static partial class ExplicitGroupsExample
            {
                [Composable(ComposableMode.ExplicitGroups)]
                public static void Content(int value)
                {
                    if (value > 0)
                    {
                        ChildHost.Child();
                    }
                }
            }
            """;

        IReadOnlyList<(string HintName, string Source)> results =
            GeneratorTestHelper.RunGenerator(source);

        string restartable = FindOutput(results, "RestartableExample");
        Assert.Contains("StartRestartableGroup", restartable);
        Assert.Contains(".Changed(value)", restartable);
        Assert.Contains(".Skipping", restartable);
        Assert.Contains("UpdateScope", restartable);

        string readOnly = FindOutput(results, "ReadOnlyExample");
        Assert.DoesNotContain("StartRestartableGroup", readOnly);
        Assert.DoesNotContain("StartReplaceableGroup", readOnly);
        Assert.DoesNotContain(".Changed(", readOnly);
        Assert.DoesNotContain(".Skipping", readOnly);
        Assert.DoesNotContain("UpdateScope", readOnly);

        string inline = FindOutput(results, "InlineExample");
        Assert.DoesNotContain("StartRestartableGroup", inline);
        Assert.Contains("StartReplaceableGroup", inline);
        Assert.Contains("byte __value_state = __changed[0]", inline);
        Assert.DoesNotContain(".Changed(", inline);
        Assert.DoesNotContain(".Skipping", inline);
        Assert.DoesNotContain("UpdateScope", inline);

        string nonSkippable = FindOutput(results, "NonSkippableExample");
        Assert.Contains("StartRestartableGroup", nonSkippable);
        Assert.Contains("byte __value_state = __changed[0]", nonSkippable);
        Assert.DoesNotContain(".Changed(", nonSkippable);
        Assert.DoesNotContain(".Skipping", nonSkippable);
        Assert.Contains("UpdateScope", nonSkippable);

        string nonRestartable = FindOutput(results, "NonRestartableExample");
        Assert.DoesNotContain("StartRestartableGroup", nonRestartable);
        Assert.Contains("StartReplaceableGroup", nonRestartable);
        Assert.Contains("byte __value_state = __changed[0]", nonRestartable);
        Assert.DoesNotContain(".Changed(", nonRestartable);
        Assert.DoesNotContain(".Skipping", nonRestartable);
        Assert.DoesNotContain("UpdateScope", nonRestartable);

        string explicitGroups = FindOutput(results, "ExplicitGroupsExample");
        Assert.DoesNotContain("StartRestartableGroup", explicitGroups);
        Assert.DoesNotContain("StartReplaceableGroup", explicitGroups);
        Assert.DoesNotContain(".Changed(", explicitGroups);
        Assert.DoesNotContain(".Skipping", explicitGroups);
        Assert.DoesNotContain("UpdateScope", explicitGroups);
    }

    [Fact]
    public void UnstableRestartableForwardsRawStatesWithoutGeneratingSkipChecks()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class ChildHost
            {
                [Composable]
                public static void Child(int stable, [Composable] Action callback)
                {
                    callback();
                }
            }

            public static partial class MixedHost
            {
                [Composable]
                public static void Mixed(
                    int stable,
                    object unstable,
                    [Composable] Action callback)
                {
                    ChildHost.Child(stable, callback);
                }
            }
            """;

        IReadOnlyList<(string HintName, string Source)> results =
            GeneratorTestHelper.RunGenerator(source);
        string generated = FindOutput(results, "MixedHost");

        Assert.Contains("byte __stable_state = __changed[0]", generated);
        Assert.Contains("byte __unstable_state = __changed[1]", generated);
        Assert.Contains("byte __callback_state = __changed[2]", generated);
        Assert.DoesNotContain(".Changed(", generated);
        Assert.DoesNotContain(".Skipping", generated);
        Assert.DoesNotContain("SkipToGroupEnd", generated);
        Assert.Contains(
            "stackalloc byte[] { __stable_state, __callback_state }",
            generated);
    }

    [Fact]
    public void InlineModesDoNotAllocateCapturingLambdaWrappers()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Example
            {
                [Composable]
                public static void RegularHost([Composable] Action content) { content(); }

                [Composable]
                public static void ReadOnlyHost(
                    [Composable(ComposableMode.ReadOnly)] Action content) { content(); }

                [Composable(ComposableMode.Inline)]
                public static void InlineHost([Composable] Action content) { content(); }

                [Composable]
                public static void ReadOnlyInlineHost(
                    [Composable(ComposableMode.ReadOnlyInline)] Action content) { content(); }

                [Composable]
                public static void Leaf(int value) { }

                [Composable(ComposableMode.ReadOnly)]
                public static void Read(int value) { }

                [Composable]
                public static void Use(int captured)
                {
                    RegularHost(() => Leaf(captured));
                    ReadOnlyHost(() => Read(captured));
                    InlineHost(() => Leaf(captured));
                    ReadOnlyInlineHost(() => Read(captured));
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorTestHelper.GetOutputCompilationDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, Count(generated, "ComposeHelpers.GetLambda("));
        Assert.Equal(1, Count(generated, "ComposeHelpers.GetReadonlyLambda("));
    }

    [Fact]
    public void InlineParameterMayOnlyStayInsideInlineCallChain()
    {
        const string valid = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Valid
            {
                [Composable(ComposableMode.Inline)]
                public static void Target([Composable] Action content) { content(); }

                [Composable(ComposableMode.Inline)]
                public static void Forward([Composable] Action content)
                {
                    Target(content);
                    Target(() => content());
                }
            }
            """;
        const string invalid = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Invalid
            {
                [Composable]
                public static void Regular([Composable] Action content) { content(); }

                [Composable(ComposableMode.Inline)]
                public static void Escape([Composable] Action content)
                {
                    Action saved = content;
                    Regular(content);
                }
            }
            """;

        Assert.DoesNotContain(
            GeneratorTestHelper.GetDiagnostics(valid),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.True(
            GeneratorTestHelper.GetDiagnostics(invalid)
                .Count(diagnostic => diagnostic.Id == "DNC021") >= 2);
    }

    [Fact]
    public void InvalidModesAreDiagnosedForMethodsParametersAndUnknownValues()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Invalid
            {
                [Composable(ComposableMode.ReadOnlyInline)]
                public static void InvalidMethodMode() { }

                [Composable]
                public static void InvalidParameterMode(
                    [Composable(ComposableMode.NonSkippable)] Action content) { }

                [Composable]
                public static void InvalidParameterTarget([Composable] int value) { }

                [Composable((ComposableMode)999)]
                public static void UnknownMode() { }
            }
            """;

        Assert.Equal(
            4,
            GeneratorTestHelper.GetDiagnostics(source)
                .Count(diagnostic => diagnostic.Id == "DNC020"));
    }

    [Fact]
    public void InlineVirtualMethodAndOverrideModeChangesAreDiagnosed()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public partial class Base
            {
                [Composable]
                public virtual void Content(
                    [Composable(ComposableMode.ReadOnly)] Action content) { }
            }

            public partial class Derived : Base
            {
                [Composable(ComposableMode.Inline)]
                public override void Content([Composable] Action content) { }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DNC022");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DNC023");
    }

    [Fact]
    public void RemovingComposableContractFromOverrideIsDiagnosed()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public partial class Base
            {
                [Composable]
                public virtual void Content() { }
            }

            public partial class Derived : Base
            {
                public override void Content() { }
            }
            """;

        Assert.Contains(
            GeneratorTestHelper.GetDiagnostics(source),
            diagnostic => diagnostic.Id == "DNC023");
    }

    [Fact]
    public void RegularComposableCallbackParticipatesInChangeChecksAndSkipping()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Example
            {
                [Composable]
                public static void Host([Composable] Action content) { content(); }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);

        Assert.Contains("__content_state = __ctx.Changed(content)", generated);
        Assert.Contains("__content_state == DotNetCompose.Runtime.ComposableArgumentsState.Same", generated);
        Assert.Contains("__ctx.Skipping", generated);
    }

    [Fact]
    public void ModesFromRuntimeAssemblyDriveReadonlyAndInlineLowering()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Example
            {
                private static readonly CompositionLocal<int> Local =
                    Composables.CompositionLocalOf(() => 0);

                [Composable(ComposableMode.ReadOnly)]
                public static int Read()
                {
                    return Local.Current();
                }

                [Composable]
                public static void Leaf(int value) { }

                [Composable]
                public static void Content(int captured)
                {
                    Composables.ComposeNode(
                        () => new object(),
                        _ => { },
                        () => Composables.Key(captured, () => Leaf(captured)));
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorTestHelper.GetOutputCompilationDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("ComposeHelpers.GetLambda(", generated);
        Assert.DoesNotContain("ComposeHelpers.GetReadonlyLambda(", generated);

        int readStart = generated.IndexOf("int Read(", StringComparison.Ordinal);
        int readEnd = generated.IndexOf("void Leaf(", readStart, StringComparison.Ordinal);
        string readMethod = generated.Substring(readStart, readEnd - readStart);
        Assert.DoesNotContain("StartRestartableGroup", readMethod);
        Assert.Contains("Local.Current(__ctx", readMethod);
    }

    private static string FindOutput(
        IReadOnlyList<(string HintName, string Source)> results,
        string className)
    {
        return Assert.Single(
            results,
            result => result.Source.Contains($"class {className}", StringComparison.Ordinal)).Source;
    }

    private static int Count(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }
}
