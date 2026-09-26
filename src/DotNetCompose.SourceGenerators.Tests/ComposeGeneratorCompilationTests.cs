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
    public void InstanceComposable_GeneratesHiddenOverloadAndStaticBridge()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public partial class Box<T> where T : class
            {
                [Composable]
                public virtual void Render<U>(U value) { }

                [Composable]
                public void Calls(Box<T> other)
                {
                    Render(1);
                    this.Render("two");
                    other.Render(3);
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("partial class Box<T> where T : class", generated);
        Assert.Contains("EditorBrowsableState.Never", generated);
        Assert.Contains("static void Render<U>", generated);
        Assert.Contains("global::TestNs.Box<T> __instance", generated);
        Assert.Contains("this.Render(\"two\", __ctx", generated);
        Assert.Contains("other.Render(3, __ctx", generated);
    }

    [Fact]
    public void ReadOnlyComposable_HasNoCompositionProtocolAndInjectsCurrentContext()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public sealed class DefaultValue : IDefaultValueProvider
            {
                public static int Value => 5;
            }

            public static partial class ReadOnlyExample
            {
                [Composable(ComposableMode.ReadOnly)]
                public static int Read([Default<DefaultValue>] int value = default)
                {
                    if (Composables.CurrentContext() == null) return -1;
                    return value;
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("DefaultValue.Value", generated);
        Assert.Contains("__ctx == null", generated);
        Assert.DoesNotContain("StartRestartableGroup", generated);
        Assert.DoesNotContain("StartReplaceableGroup", generated);
        Assert.DoesNotContain("StartMovableGroup", generated);
        Assert.DoesNotContain(".Changed(", generated);
        Assert.DoesNotContain(".Skipping", generated);
        Assert.DoesNotContain("UpdateScope", generated);
        Assert.DoesNotContain("Builders.CurrentContext", generated);
    }

    [Fact]
    public void ReadOnlyLambdas_UseStoredMethodOrReadonlyHelper()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class ReadOnlyLambdas
            {
                [Composable]
                public static void Host([Composable(ComposableMode.ReadOnly)] Action content) { content(); }

                [Composable(ComposableMode.ReadOnly)]
                public static void Read(int value) { }

                [Composable]
                public static void Use(int captured)
                {
                    Host(() => Read(captured));
                    Host(() => Read(1));
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(1, generated.Split("GetReadonlyLambda").Length - 1);
        Assert.Contains("static class __StoredLambda", generated);
        Assert.Contains("__StoredLambda", generated);
    }

    [Fact]
    public void ReadOnlyComposable_RejectsMutableComposableCall()
    {
        const string source = """
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static partial class Invalid
            {
                [Composable] public static void Mutable() { }
                [Composable(ComposableMode.ReadOnly)] public static void Read() { Mutable(); }
            }
            """;

        Assert.Contains(GeneratorTestHelper.GetDiagnostics(source), diagnostic => diagnostic.Id == "DNC016");
    }

    [Fact]
    public void ReadOnlyComposable_RejectsNonReadOnlyDelegateParameter()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static partial class Invalid
            {
                [Composable(ComposableMode.ReadOnly)]
                public static void Host([Composable] Action content) { }

                [Composable(ComposableMode.ReadOnly)]
                public static void Read() { Host(() => { }); }
            }
            """;

        Assert.Contains(GeneratorTestHelper.GetDiagnostics(source), diagnostic => diagnostic.Id == "DNC016");
    }

    [Fact]
    public void ReadOnlyDelegate_RequiresProvableContractButAllowsMatchingForwarding()
    {
        const string valid = """
            using System;
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static partial class Valid
            {
                [Composable] public static void Host([Composable(ComposableMode.ReadOnly)] Action content) { content(); }
                [Composable] public static void Forward([Composable(ComposableMode.ReadOnly)] Action content) { Host(content); }
            }
            """;
        const string invalid = """
            using System;
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static partial class Invalid
            {
                [Composable] public static void Host([Composable(ComposableMode.ReadOnly)] Action content) { content(); }
                [Composable(ComposableMode.ReadOnly)] public static void Read() { }
                [Composable] public static void Forward([Composable] Action content) { Host(content); Host(Read); }
            }
            """;

        Assert.DoesNotContain(GeneratorTestHelper.GetDiagnostics(valid), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(GeneratorTestHelper.GetDiagnostics(invalid), diagnostic => diagnostic.Id == "DNC017");
    }

    [Fact]
    public void ReadOnlyOverrideAndGeneratedSignatureConflict_AreDiagnosed()
    {
        const string readOnlyOverride = """
            using DotNetCompose.Runtime;
            namespace TestNs;
            public partial class Base
            {
                [Composable(ComposableMode.ReadOnly)] public virtual void Content() { }
            }
            public partial class Derived : Base
            {
                [Composable] public override void Content() { }
            }
            """;
        const string signatureConflict = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            namespace TestNs;
            public partial class Conflict
            {
                [Composable] public void Content(int value) { }
                public void Content(int value, IComposerContext context,
                    ComposableArgumentsState changed, ComposableArgumentsDefaultState defaults) { }
            }
            """;

        Assert.Contains(GeneratorTestHelper.GetDiagnostics(readOnlyOverride), diagnostic => diagnostic.Id == "DNC023");
        Assert.Contains(GeneratorTestHelper.GetDiagnostics(signatureConflict), diagnostic => diagnostic.Id == "DNC018");
    }

    [Fact]
    public void ReferencedCompositionLocalCurrent_DirectCallIsDiagnosed()
    {
        const string source = """
            using DotNetCompose.Runtime;
            namespace TestNs;
            public static class Invalid
            {
                public static int Read(CompositionLocal<int> local) => local.Current();
            }
            """;

        Assert.Contains(GeneratorTestHelper.GetDiagnostics(source), diagnostic => diagnostic.Id == "DNC015");
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
