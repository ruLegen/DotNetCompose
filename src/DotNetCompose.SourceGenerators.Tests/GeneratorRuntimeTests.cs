using DotNetCompose.Runtime.Composer;
using Microsoft.CodeAnalysis.Emit;

namespace DotNetCompose.SourceGenerators.Tests;

public class GeneratorRuntimeTests
{
    public sealed class Applier : IApplier<object>
    {
        public object Current { get; } = new object();
        public void OnBeginChanges() { }
        public void OnEndChanges() { }
        public void Down(object node) { }
        public void Up() { }
        public void InsertTopDown(int index, object instance) { }
        public void InsertBottomUp(int index, object instance) { }
        public void Remove(int index, int count) { }
        public void Move(int from, int to, int count) { }
        public void Clear() { }
        public void Apply(Action<object, object?> block, object? value) => block(Current, value);
    }

    [Fact]
    public void GeneratedRestartPreservesSlotsAndDoesNotDirtyChildren()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public sealed class TestDefaultProvider : IDefaultValueProvider
            {
                public static int Value => Example.DefaultProvidedValue;
            }

            public static partial class Example
            {
                public static SnapshotMutableState<int> ParentState = Composables.CreateMutableState(0);
                public static SnapshotMutableState<int> ChildState = Composables.CreateMutableState(0);
                public static SnapshotMutableState<int> DefaultState = Composables.CreateMutableState(0);
                public static object Last;
                public static int ParentExecutions;
                public static int ChildExecutions;
                public static int StaticExecutions;
                public static int DefaultExecutions;
                public static int ParentValue;
                public static int ChildValue;
                public static int DefaultValue;
                public static int DefaultProvidedValue = 41;
                public static int Parameter = 7;

                [Composable]
                public static void Parent(int parameter)
                {
                    Last = Composables.Remember("stable", () => new object());
                    ParentValue = parameter + ParentState.Value;
                    ParentExecutions++;
                    Child(parameter);
                    StaticChild(11);
                    WithDefault();
                }

                [Composable]
                public static void Child(int value)
                {
                    ChildValue = value + ChildState.Value;
                    ChildExecutions++;
                }

                [Composable]
                public static void StaticChild(int value) { StaticExecutions += value > 0 ? 1 : 0; }

                [Composable]
                public static void WithDefault([Default<TestDefaultProvider>] int value = default)
                {
                    DefaultValue = value + DefaultState.Value;
                    DefaultExecutions++;
                }

                public static int Run()
                {
                    _ = ParentState.Value;
                    _ = ChildState.Value;
                    _ = DefaultState.Value;
                    using (Composition<object> composition = new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((c, changed, defaults) => Builders.Parent(Parameter, c, changed, defaults));
                        object first = Last;
                        if (ParentExecutions != 1 || ChildExecutions != 1 || StaticExecutions != 1 ||
                            DefaultExecutions != 1 || DefaultValue != 41) return 1;

                        ParentState.Value = 2;
                        if (!composition.Recompose()) return 2;
                        composition.ApplyChanges();
                        if (!ReferenceEquals(first, Last) || ParentExecutions != 2 || ChildExecutions != 1 ||
                            StaticExecutions != 1 || DefaultExecutions != 1 || ParentValue != 9) return 3;

                        ChildState.Value = 3;
                        if (!composition.Recompose()) return 4;
                        composition.ApplyChanges();
                        if (ParentExecutions != 2 || ChildExecutions != 2 || StaticExecutions != 1 ||
                            DefaultExecutions != 1 || ChildValue != 10) return 5;

                        DefaultProvidedValue = 52;
                        DefaultState.Value = 1;
                        if (!composition.Recompose()) return 6;
                        composition.ApplyChanges();
                        if (ParentExecutions != 2 || ChildExecutions != 2 || StaticExecutions != 1 ||
                            DefaultExecutions != 2 || DefaultValue != 53) return 7;

                        Parameter = 8;
                        composition.SetContent((c, changed, defaults) => Builders.Parent(Parameter, c, changed, defaults));
                        return ReferenceEquals(first, Last) && ParentExecutions == 3 && ChildExecutions == 3 &&
                            StaticExecutions == 1 && DefaultExecutions == 2 && ParentValue == 10 && ChildValue == 11 ? 0 : 8;
                    }
                }
            }
            """;
        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        string counters = string.Join(", ", new[] { "ParentExecutions", "ChildExecutions", "StaticExecutions", "DefaultExecutions", "ParentValue", "ChildValue", "DefaultValue" }
            .Select(name => $"{name}={example.GetField(name)!.GetValue(null)}"));
        Assert.True(Equals(0, result), $"Stage={result}; {counters}");
    }

    [Fact]
    public void GeneratedCallersKeepComparisonSlotOwnershipStable()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.SlotTable;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                public static SnapshotMutableState<int> KnownState = Composables.CreateMutableState(0);
                public static SnapshotMutableState<int> UnknownState = Composables.CreateMutableState(0);
                public static object KnownRemembered;
                public static object UnknownRemembered;
                public static int KnownExecutions;
                public static int UnknownExecutions;
                public static int KnownValue;
                public static int UnknownValue;
                public static int Parameter = 7;

                [Composable]
                public static void Parent(int value)
                {
                    KnownChild(value);
                    UnknownChild(GetValue(value));
                }

                [Composable]
                public static void KnownChild(int value)
                {
                    KnownRemembered = Composables.Remember("known", () => new object());
                    KnownValue = value + KnownState.Value;
                    KnownExecutions++;
                }

                [Composable]
                public static void UnknownChild(int value)
                {
                    UnknownRemembered = Composables.Remember("unknown", () => new object());
                    UnknownValue = value + UnknownState.Value;
                    UnknownExecutions++;
                }

                private static int GetValue(int value) => value;

                private static bool HasExpectedSlotShape(Composition<object> composition)
                {
                    using ComposerSlotTable.Reader reader = composition.SlotTable.OpenReader();
                    if (reader.Size != 4) return false;
                    reader.StartGroup();
                    reader.StartGroup();
                    if (reader.GetSlotSize(reader.CurrentGroup) != 2) return false;
                    reader.SkipGroup();
                    if (reader.GetSlotSize(reader.CurrentGroup) != 3) return false;
                    reader.SkipGroup();
                    return reader.IsGroupEnd;
                }

                public static int Run()
                {
                    _ = KnownState.Value;
                    _ = UnknownState.Value;
                    using (Composition<object> composition = new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((context, changed, defaults) => Builders.Parent(Parameter, context, changed, defaults));
                        object firstKnown = KnownRemembered;
                        object firstUnknown = UnknownRemembered;
                        if (KnownExecutions != 1 || UnknownExecutions != 1 || !HasExpectedSlotShape(composition)) return 1;

                        KnownState.Value = 1;
                        if (!composition.Recompose()) return 2;
                        composition.ApplyChanges();
                        if (!ReferenceEquals(firstKnown, KnownRemembered) || !ReferenceEquals(firstUnknown, UnknownRemembered) ||
                            KnownExecutions != 2 || UnknownExecutions != 1 || KnownValue != 8 ||
                            !HasExpectedSlotShape(composition)) return 3;

                        UnknownState.Value = 2;
                        if (!composition.Recompose()) return 4;
                        composition.ApplyChanges();
                        if (!ReferenceEquals(firstKnown, KnownRemembered) || !ReferenceEquals(firstUnknown, UnknownRemembered) ||
                            KnownExecutions != 2 || UnknownExecutions != 2 || UnknownValue != 9 ||
                            !HasExpectedSlotShape(composition)) return 5;

                        Parameter = 8;
                        composition.SetContent((context, changed, defaults) => Builders.Parent(Parameter, context, changed, defaults));
                        return ReferenceEquals(firstKnown, KnownRemembered) && ReferenceEquals(firstUnknown, UnknownRemembered) &&
                            KnownExecutions == 3 && UnknownExecutions == 3 && KnownValue == 9 && UnknownValue == 10 &&
                            HasExpectedSlotShape(composition) ? 0 : 6;
                    }
                }
            }
            """;

        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        string counters = string.Join(", ", new[] { "KnownExecutions", "UnknownExecutions", "KnownValue", "UnknownValue" }
            .Select(name => $"{name}={example.GetField(name)!.GetValue(null)}"));
        Assert.True(Equals(0, result), $"Stage={result}; {counters}");
    }

    [Fact]
    public void GeneratedCompositionLocalProviderUsesTheActiveComposer()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                public static readonly ProvidableCompositionLocal<int> LocalValue =
                    Composables.CompositionLocalOf(() => -1);
                public static readonly ProvidableCompositionLocal<string> LocalText =
                    Composables.StaticCompositionLocalOf(() => "default");
                public static readonly SnapshotMutableState<int> Source = Composables.CreateMutableState(1);
                public static int ReaderExecutions;
                public static int Seen;
                public static string SeenText;

                [Composable]
                public static void Parent()
                {
                    int value = Source.Value;
                    Composables.CompositionLocalProvider(
                        new ProvidedValue[] { LocalValue.Provides(value), LocalText.Provides("outer") },
                        () => Composables.CompositionLocalProvider(LocalText.Provides("inner"), () => Reader()));
                }

                [Composable]
                public static void Reader()
                {
                    ReaderExecutions++;
                    Seen = LocalValue.Current;
                    SeenText = LocalText.Current;
                }

                public static int Run()
                {
                    _ = Source.Value;
                    using (Composition<object> composition = new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((context, changed, defaults) => Builders.Parent(context, changed, defaults));
                        if (ReaderExecutions != 1 || Seen != 1 || SeenText != "inner" || composition.HasInvalidations) return 1;
                        Source.Value = 2;
                        if (!composition.Recompose()) return 2;
                        if (Seen != 2) return 3;
                        composition.ApplyChanges();
                        if (ReaderExecutions != 2 || Seen != 2 || SeenText != "inner" || composition.HasInvalidations) return 4;
                        return composition.Recompose() ? 5 : 0;
                    }
                }
            }
            """;

        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        Assert.True(Equals(0, result),
            $"Stage={result}; ReaderExecutions={example.GetField("ReaderExecutions")!.GetValue(null)}; Seen={example.GetField("Seen")!.GetValue(null)}");
    }

    private static Type CompileExample(string source)
    {
        IEnumerable<string> paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ComposableAttribute).Assembly.Location)
            .Append(typeof(GeneratorRuntimeTests).Assembly.Location)
            .Distinct();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RuntimeIntegration_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComposeSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        using MemoryStream stream = new MemoryStream();
        EmitResult emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        return assembly.GetType("Integration.Example")!;
    }
}
