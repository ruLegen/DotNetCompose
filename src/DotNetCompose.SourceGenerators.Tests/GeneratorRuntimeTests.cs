using DotNetCompose.Runtime.Composer;

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
    public void GeneratedRestartAndCallerFlagsPreserveRememberSlots()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                public static SnapshotMutableState<int> State = Composables.CreateMutableState(0);
                public static object Last;
                public static int Executions;
                public static int Value;
                public static int ChildValue;

                [Composable]
                public static void Counter(int parameter)
                {
                    Last = Composables.Remember("stable", () => new object());
                    Value = parameter + State.Value;
                    Executions++;
                    Composables.ComposeNode(() => new object(), node => { }, () => Child(parameter));
                    Composables.Key("fixed", () => StaticChild());
                }

                [Composable]
                public static void Child(int value) { ChildValue = value; }

                [Composable]
                public static void StaticChild() { }

                public static bool Run()
                {
                    using (Composition<object> composition = new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((c, changed, defaults) => Builders.Counter(7, c, changed, defaults));
                        object first = Last;
                        State.Value = 2;
                        if (!composition.Recompose()) return false;
                        composition.ApplyChanges();
                        if (!ReferenceEquals(first, Last) || Executions != 2 || Value != 9) return false;
                        composition.SetContent((c, changed, defaults) => Builders.Counter(7, c,
                            new ComposableArgumentsState(new byte[] { ComposableArgumentsState.Same }), defaults));
                        if (!ReferenceEquals(first, Last) || Executions != 2) return false;
                        composition.SetContent((c, changed, defaults) => Builders.Counter(8, c,
                            new ComposableArgumentsState(new byte[] { ComposableArgumentsState.Different }), defaults));
                        return ReferenceEquals(first, Last) && Executions == 3 && Value == 10 && ChildValue == 8;
                    }
                }
            }
            """;
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(ComposableAttribute).Assembly.Location)
            .Append(typeof(GeneratorRuntimeTests).Assembly.Location).Distinct();
        var compilation = CSharpCompilation.Create("RuntimeIntegration_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ComposeSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(true, assembly.GetType("Integration.Example")!.GetMethod("Run")!.Invoke(null, null));
    }
}
