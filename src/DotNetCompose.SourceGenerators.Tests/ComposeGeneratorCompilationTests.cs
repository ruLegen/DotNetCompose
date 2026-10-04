using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace DotNetCompose.SourceGenerators.Tests;

public class ComposeGeneratorCompilationTests
{
    [Fact]
    public void StoredLambdasInDifferentMethodsHaveUniqueNames()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public static partial class Example
            {
                [Composable]
                public static void Host([Composable] Action content)
                {
                    content();
                }

                [Composable]
                public static void Leaf(int value)
                {
                }

                [Composable]
                public static void First()
                {
                    Host(() => Leaf(1));
                    Host(() => Leaf(2));
                }

                [Composable]
                public static void Second()
                {
                    Host(() => Leaf(3));
                }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);
        MatchCollection declarations = Regex.Matches(generated, @"public static void (__Lambda_\d+_\d+)\(");

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(3, declarations.Count);
        Assert.Equal(3, declarations.Select(match => match.Groups[1].Value).Distinct().Count());
    }

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
    public void GeneratedBuilderCallResolvesInOutputCompilation()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;

            namespace TestNs;

            public static partial class Example
            {
                [Composable]
                public static void FooName() { }

                public static void Run(IComposerContext context) => Builders.FooName(context);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
        Assert.Contains("MyIntProvider.Create()", result);
        Assert.Contains("ShouldUseDefault", result);
    }

    [Fact]
    public void ComposableWithDefault_GeneratedCSharpCompiles()
    {
        string source = GeneratorTestHelper.LoadSource("ComposableWithDefault.cs");
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("public int Value => 1;")]
    [InlineData("public int Create() => 1;")]
    [InlineData("public static string Create() => \"wrong\";")]
    [InlineData("private static int Create() => 1;")]
    [InlineData("[Composable] public static int Create() { return 1; }")]
    public void InvalidDefaultProvider_ReportsDiagnostic(string providerMember)
    {
        string source = $$"""
            using DotNetCompose.Runtime;
            namespace TestNs;
            public class Provider : IDefaultValueProvider
            {
                {{providerMember}}
            }
            public static partial class Example
            {
                [Composable]
                public static void Render([Default<Provider>] int value = default) { }
            }
            """;

        Assert.Contains(GeneratorTestHelper.GetDiagnostics(source), diagnostic => diagnostic.Id == "DNC024");
    }


    [Fact]
    public void InstanceComposable_GeneratesOnlyHiddenInstanceOverload()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public partial class Box<T> where T : class
            {
                [Composable]
                public virtual void Render<U>(U value) where U : IComparable<U> { }

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
        Assert.Contains("public virtual void Render<U>", generated);
        Assert.Contains("where U : IComparable<U>", generated);
        Assert.DoesNotContain("partial class Builders", generated);
        Assert.DoesNotContain("__instance", generated);
        Assert.Contains("this.Render(\"two\", __ctx", generated);
        Assert.Contains("other.Render(3, __ctx", generated);
    }

    [Fact]
    public void MixedComposables_KeepMethodsAndStoredLambdasInTheirOwnContainers()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;

            namespace TestNs;

            public partial class Mixed
            {
                [Composable]
                private void InstanceHost([Composable] Action content) { content(); }

                [Composable]
                public void InstanceRoot(int value)
                {
                    InstanceHost(() => StaticLeaf<string>(1));
                    InstanceHost(() => InstanceLeaf(value));
                }

                [Composable]
                protected void InstanceLeaf(int value) { }

                [Composable]
                private static void StaticHost([Composable] Action content) { content(); }

                [Composable]
                public static void StaticRoot() { StaticHost(() => StaticLeaf<string>(2)); }

                [Composable]
                public static void StaticLeaf<U>(int value) where U : class { }
            }
            """;

        string generated = GeneratorTestHelper.RunSingleGenerator(source);
        ImmutableArray<Diagnostic> diagnostics = GeneratorTestHelper.GetOutputCompilationDiagnostics(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        ClassDeclarationSyntax containingType = CSharpSyntaxTree.ParseText(generated).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Mixed");
        ClassDeclarationSyntax builders = containingType.Members.OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Builders");
        ClassDeclarationSyntax instanceLambdas = containingType.Members.OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "__StoredLambda");
        ClassDeclarationSyntax staticLambdas = builders.Members.OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "__StoredLambda");

        Assert.Equal(new[] { "InstanceHost", "InstanceRoot", "InstanceLeaf" },
            containingType.Members.OfType<MethodDeclarationSyntax>().Select(method => method.Identifier.ValueText));
        Assert.Equal(new[] { "StaticHost", "StaticRoot", "StaticLeaf" },
            builders.Members.OfType<MethodDeclarationSyntax>().Select(method => method.Identifier.ValueText));
        Assert.Single(instanceLambdas.Members.OfType<MethodDeclarationSyntax>());
        Assert.Single(staticLambdas.Members.OfType<MethodDeclarationSyntax>());
        Assert.Contains("private void InstanceHost", generated);
        Assert.Contains("protected void InstanceLeaf", generated);
        Assert.Contains("ComposeHelpers.GetLambda", generated);
        Assert.DoesNotContain("__instance", generated);
    }

    [Fact]
    public void ReadOnlyComposable_HasNoCompositionProtocolAndInjectsCurrentContext()
    {
        const string source = """
            using DotNetCompose.Runtime;

            namespace TestNs;

            public sealed class DefaultValue : IDefaultValueProvider
            {
                public static int Create() => 5;
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
        Assert.Contains("DefaultValue.Create()", generated);
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
        Assert.Contains("static partial class __StoredLambda", generated);
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
