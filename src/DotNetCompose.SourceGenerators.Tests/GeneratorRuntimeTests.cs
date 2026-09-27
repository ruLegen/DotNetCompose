using DotNetCompose.Runtime.Composer;
using Microsoft.CodeAnalysis.Emit;

namespace DotNetCompose.SourceGenerators.Tests;

public class GeneratorRuntimeTests
{
    public sealed class Applier : IApplier<object>
    {
        public object Current { get; } = new object();
        public void OnBeginChanges()
        {
        }
        public void OnEndChanges()
        {
        }
        public void Down(object node)
        {
        }
        public void Up()
        {
        }
        public void InsertTopDown(int index, object instance)
        {
        }
        public void InsertBottomUp(int index, object instance)
        {
        }
        public void Remove(int index, int count)
        {
        }
        public void Move(int from, int to, int count)
        {
        }
        public void Clear()
        {
        }
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
                public static int Create() => Example.DefaultProvidedValue;
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

        foreach (bool generateDiagnostics in new[] { false, true })
        {
            Type example = CompileExample(source, generateDiagnostics);
            object? result = example.GetMethod("Run")!.Invoke(null, null);
            string counters = string.Join(", ", new[] { "KnownExecutions", "UnknownExecutions", "KnownValue", "UnknownValue" }
                .Select(name => $"{name}={example.GetField(name)!.GetValue(null)}"));
            Assert.True(Equals(0, result),
                $"Diagnostics={generateDiagnostics}; Stage={result}; {counters}");
        }
    }

    [Fact]
    public void UnstableParentLeavesComparisonSlotOwnershipToChild()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.SlotTable;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                public static readonly SnapshotMutableState<int> ChildState =
                    Composables.CreateMutableState(0);
                public static readonly object Unstable = new object();
                public static object Remembered;
                public static int ParentExecutions;
                public static int ChildExecutions;
                public static int ChildValue;
                public static int Parameter = 7;

                [Composable]
                public static void Parent(int value, object unstable)
                {
                    ParentExecutions++;
                    Child(value);
                }

                [Composable]
                public static void Child(int value)
                {
                    Remembered = Composables.Remember("child", () => new object());
                    ChildValue = value + ChildState.Value;
                    ChildExecutions++;
                }

                private static bool HasExpectedSlotShape(Composition<object> composition)
                {
                    using ComposerSlotTable.Reader reader = composition.SlotTable.OpenReader();
                    if (reader.Size != 3) return false;

                    reader.StartGroup();
                    if (reader.GetSlotSize(reader.CurrentGroup) != 0) return false;

                    reader.StartGroup();
                    if (reader.GetSlotSize(reader.CurrentGroup) != 3) return false;
                    reader.SkipGroup();
                    return reader.IsGroupEnd;
                }

                public static int Run()
                {
                    _ = ChildState.Value;
                    using (Composition<object> composition =
                        new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((context, changed, defaults) =>
                            Builders.Parent(Parameter, Unstable, context, changed, defaults));
                        object firstRemembered = Remembered;
                        if (ParentExecutions != 1 || ChildExecutions != 1 ||
                            ChildValue != 7 || !HasExpectedSlotShape(composition)) return 1;

                        ChildState.Value = 1;
                        if (!composition.Recompose()) return 2;
                        composition.ApplyChanges();
                        if (ParentExecutions != 1 || ChildExecutions != 2 ||
                            ChildValue != 8 || !ReferenceEquals(firstRemembered, Remembered) ||
                            !HasExpectedSlotShape(composition)) return 3;

                        Parameter = 8;
                        composition.SetContent((context, changed, defaults) =>
                            Builders.Parent(Parameter, Unstable, context, changed, defaults));
                        return ParentExecutions == 2 && ChildExecutions == 3 &&
                            ChildValue == 9 && ReferenceEquals(firstRemembered, Remembered) &&
                            HasExpectedSlotShape(composition) ? 0 : 4;
                    }
                }
            }
            """;

        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        string counters = string.Join(
            ", ",
            new[] { "ParentExecutions", "ChildExecutions", "ChildValue" }
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
                    Seen = LocalValue.Current();
                    SeenText = LocalText.Current();
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

    [Fact]
    public void GeneratedInstanceComposablesPreserveStateReceiversGenericsAndVirtualDispatch()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public partial class Base
            {
                public int Total;

                [Composable]
                public virtual void Render<T>(T value) { Total += 1; }

                [Composable]
                public virtual void Render(string value) { Total += 2; }

                [Composable]
                public void Call(Base other)
                {
                    Render(1);
                    this.Render("value");
                    other.Render(2);
                }
            }

            public partial class Derived : Base
            {
                [Composable]
                public override void Render<T>(T value) { Total += 10; }

                [Composable]
                public override void Render(string value) { Total += 20; }
            }

            public static partial class Example
            {
                public static int Run()
                {
                    Derived instance = new Derived();
                    Base other = new Base();
                    using (Composition<object> composition = new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent((context, changed, defaults) =>
                            Base.Builders.Call(instance, other, context, changed, defaults));
                    }
                    return instance.Total == 30 && other.Total == 1 ? 0 : instance.Total * 100 + other.Total;
                }
            }
            """;

        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(0, result);
    }

    [Fact]
    public void NonSkippableProviderRunsWhileUnchangedChildSkips()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                public static readonly ProvidableCompositionLocal<int> Local =
                    Composables.CompositionLocalOf(() => 0);
                public static readonly SnapshotMutableState<int> Trigger =
                    Composables.CreateMutableState(0);
                public static int ParentExecutions;
                public static int ChildExecutions;

                [Composable]
                public static void Parent()
                {
                    _ = Trigger.Value;
                    ParentExecutions++;
                    Composables.CompositionLocalProvider(
                        Local.Provides(7),
                        () => Child(5));
                }

                [Composable]
                public static void Child(int stable)
                {
                    _ = Local.Current();
                    ChildExecutions++;
                }

                public static int Run()
                {
                    using (Composition<object> composition =
                        new Composition<object>(new GeneratorRuntimeTests.Applier()))
                    {
                        composition.SetContent(Builders.Parent);
                        if (ParentExecutions != 1 || ChildExecutions != 1) return 1;

                        Trigger.Value++;
                        if (!composition.Recompose()) return 2;
                        composition.ApplyChanges();

                        return ParentExecutions == 2 && ChildExecutions == 1 ? 0 : 3;
                    }
                }
            }
            """;

        Type example = CompileExample(source);
        object? result = example.GetMethod("Run")!.Invoke(null, null);

        Assert.True(
            Equals(0, result),
            $"Stage={result}; Parent={example.GetField("ParentExecutions")!.GetValue(null)}; " +
            $"Child={example.GetField("ChildExecutions")!.GetValue(null)}");
    }

    [Fact]
    public void GeneratedDiagnosticsExposeEventsParameterStatesAndSnapshots()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Diagnostics;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public sealed class Observer : ICompositionObserver
            {
                public readonly List<CompositionDiagnosticEvent> Events = new();
                public bool Throw;
                public void OnEvent(CompositionDiagnosticEvent value)
                {
                    Events.Add(value);
                    if (Throw) throw new InvalidOperationException("observer failure");
                }
            }

            public static partial class Example
            {
                public static readonly SnapshotMutableState<int> State = Composables.CreateMutableState(0);

                [Composable]
                public static void Child(int value)
                {
                    _ = value + State.Value;
                }

                public static int Run()
                {
                    using Composition<object> composition =
                        new Composition<object>(new GeneratorRuntimeTests.Applier());
                    using CompositionDiagnosticsSession session = composition.StartDiagnostics(
                        new CompositionDiagnosticsOptions
                        {
                            Flags = CompositionDiagnosticsFlags.All
                        });
                    Observer good = new Observer();
                    Observer bad = new Observer { Throw = true };
                    int observerErrors = 0;
                    session.ObserverError += (_, _) => observerErrors++;
                    using IDisposable goodSubscription = session.Subscribe(good);
                    using IDisposable badSubscription = session.Subscribe(bad);

                    composition.SetContent((context, changed, defaults) =>
                        Builders.Child(
                            7,
                            context,
                            new ComposableArgumentsState(stackalloc byte[]
                            {
                                ComposableArgumentsState.Static
                            }),
                            default));

                    CompositionDiagnosticsSnapshot first = session.CaptureSnapshot();
                    if (!first.IsAvailable || first.Composables.Count != 1) return 1;
                    ComposableInvocationSnapshot invocation = first.Composables[0];
                    if (invocation.InvocationId == 0 || invocation.Source.MemberName != "Child") return 2;
                    if (invocation.Parameters.Count != 1 || invocation.Parameters[0].Name != "value" ||
                        invocation.Parameters[0].State != CompositionParameterState.Static) return 3;
                    if (invocation.StateReadCount != 1 ||
                        invocation.Outcome != ComposableExecutionOutcome.Executed) return 4;
                    if (observerErrors != 1 || bad.Events.Count != 1) return 5;
                    if (!good.Events.Any(e => e.Kind == CompositionDiagnosticEventKind.ComposableEnded) ||
                        !good.Events.Any(e => e.Kind == CompositionDiagnosticEventKind.StateRead) ||
                        !good.Events.Any(e => e.Kind == CompositionDiagnosticEventKind.ApplyChangesEnded)) return 6;

                    State.Value = 1;
                    if (!composition.Recompose()) return 7;
                    composition.ApplyChanges();
                    CompositionDiagnosticsSnapshot secondSnapshot = session.CaptureSnapshot();
                    ComposableInvocationSnapshot second = secondSnapshot.Composables[0];
                    if (second.InvocationId == invocation.InvocationId ||
                        secondSnapshot.PassId == first.PassId ||
                        second.Outcome != ComposableExecutionOutcome.Executed ||
                        second.StateReadCount != 1) return 8;
                    if (bad.Events.Count != 1 || observerErrors != 1) return 9;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source, generateDiagnostics: true);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(0, result);
    }

    [Fact]
    public void GeneratedEarlyReturnClosesControlFlowGroupBeforeDiagnosticsEnd()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Diagnostics;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public static partial class Example
            {
                [Composable]
                public static void Child()
                {
                }

                [Composable]
                public static void Early(int value)
                {
                    if (value == 0)
                    {
                        Child();
                        return;
                    }
                }

                public static int Run()
                {
                    using Composition<object> composition =
                        new Composition<object>(new GeneratorRuntimeTests.Applier());
                    using CompositionDiagnosticsSession session = composition.StartDiagnostics(
                        new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.All });

                    composition.SetContent((context, changed, defaults) =>
                        Builders.Early(
                            0,
                            context,
                            new ComposableArgumentsState(stackalloc byte[]
                            {
                                ComposableArgumentsState.Static
                            }),
                            default));

                    ComposableInvocationSnapshot early = session.CaptureSnapshot().Composables[0];
                    if (early.Source.MemberName != "Early" ||
                        early.Outcome != ComposableExecutionOutcome.Executed) return 1;
                    if (early.Children.Count != 1 ||
                        early.Children[0].Source.MemberName != "Child" ||
                        early.Children[0].Outcome != ComposableExecutionOutcome.Executed) return 2;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source, generateDiagnostics: true);
        object? result = example.GetMethod("Run")!.Invoke(null, null);
        Assert.Equal(0, result);
    }

    [Fact]
    public void DefaultsSkipIndependentlyAndTrackStateThroughInlineProvider()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public partial class NumberProvider : IDefaultValueProvider
            {
                public static SnapshotMutableState<int> State = Composables.CreateMutableState(10);
                public static int Calls;

                [Composable(ComposableMode.Inline)]
                public static int Create()
                {
                    Calls++;
                    return Example.ReadNumber();
                }
            }

            public static partial class Example
            {
                public static SnapshotMutableState<int> Ordinary = Composables.CreateMutableState(1);
                public static SnapshotMutableState<bool> Omit = Composables.CreateMutableState(true);
                public static SnapshotMutableState<int> BodyState = Composables.CreateMutableState(0);
                public static int RenderCalls;
                public static int ChildCalls;
                public static int Seen;
                public static int ChildSeen;

                [Composable]
                public static int ReadNumber() { return NumberProvider.State.Value; }

                [Composable]
                public static void Render(int ordinary, [Default<NumberProvider>] int value = default)
                {
                    _ = BodyState.Value;
                    RenderCalls++;
                    Seen = value;
                    Child(value);
                }

                [Composable]
                public static void Child(int value)
                {
                    ChildCalls++;
                    ChildSeen = value;
                }

                private static bool SlotsOwnedByTheirGroups(Composition<object> composition)
                {
                    using var reader = composition.SlotTable.OpenReader();
                    int renderGroup = 1;
                    int defaultsGroup = renderGroup + 1;
                    int childGroup = reader.GetGroupEnd(defaultsGroup);
                    return reader.GetSlotSize(renderGroup) == 3 &&
                        reader.GetSlotSize(defaultsGroup) == 1 &&
                        reader.GetSlotSize(childGroup) == 1;
                }

                public static int Run()
                {
                    _ = Ordinary.Value;
                    _ = Omit.Value;
                    _ = BodyState.Value;
                    _ = NumberProvider.State.Value;
                    Snapshot.SendApplyNotifications();
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) =>
                    {
                        bool omit = Omit.Value;
                        Builders.Render(Ordinary.Value, default, ctx, default,
                            new ComposableArgumentsDefaultState(new byte[] { omit ? (byte)1 : (byte)0 }));
                    });
                    if (NumberProvider.Calls != 1 || RenderCalls != 1 || ChildSeen != 10) return 1;
                    if (!SlotsOwnedByTheirGroups(composition)) return 10;

                    Ordinary.Value = 2;
                    if (!composition.Recompose()) return 2;
                    composition.ApplyChanges();
                    if (NumberProvider.Calls != 1) return 31;
                    if (RenderCalls != 2) return 32;
                    if (ChildCalls != 1) return 33;
                    if (Seen != 10) return 34;
                    if (!SlotsOwnedByTheirGroups(composition)) return 11;

                    NumberProvider.State.Value = 20;
                    if (!composition.Recompose()) return 4;
                    composition.ApplyChanges();
                    if (NumberProvider.Calls != 2) return 51;
                    if (ChildCalls != 2) return 52;
                    if (ChildSeen != 20) return 53;

                    Omit.Value = false;
                    if (!composition.Recompose()) return 6;
                    composition.ApplyChanges();
                    if (NumberProvider.Calls != 2 || Seen != 0 || ChildSeen != 0) return 7;
                    if (!SlotsOwnedByTheirGroups(composition)) return 12;

                    NumberProvider.State.Value = 30;
                    if (composition.Recompose()) return 14;

                    Omit.Value = true;
                    if (!composition.Recompose()) return 8;
                    composition.ApplyChanges();
                    if (NumberProvider.Calls != 3 || Seen != 30 || ChildSeen != 30) return 9;
                    if (!SlotsOwnedByTheirGroups(composition)) return 13;

                    BodyState.Value = 1;
                    if (!composition.Recompose()) return 15;
                    composition.ApplyChanges();
                    if (NumberProvider.Calls != 4 || ChildSeen != 30) return 16;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void DefaultsTrackDynamicAndStaticCompositionLocals()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public partial class DynamicProvider : IDefaultValueProvider
            {
                [Composable(ComposableMode.Inline)]
                public static int Create() { return Example.DynamicLocal.Current(); }
            }

            public partial class StaticProvider : IDefaultValueProvider
            {
                [Composable(ComposableMode.Inline)]
                public static string Create() { return Example.StaticLocal.Current(); }
            }

            public static partial class Example
            {
                public static readonly ProvidableCompositionLocal<int> DynamicLocal =
                    Composables.CompositionLocalOf(() => -1);
                public static readonly ProvidableCompositionLocal<string> StaticLocal =
                    Composables.StaticCompositionLocalOf(() => "none");
                public static readonly SnapshotMutableState<int> Dynamic = Composables.CreateMutableState(1);
                public static readonly SnapshotMutableState<string> Static = Composables.CreateMutableState("first");
                public static int RenderCalls;
                public static int ChildCalls;
                public static string Seen;

                [Composable]
                public static void Root()
                {
                    Composables.CompositionLocalProvider(
                        new ProvidedValue[] { DynamicLocal.Provides(Dynamic.Value), StaticLocal.Provides(Static.Value) },
                        () => Render());
                }

                [Composable]
                public static void Render(
                    [Default<DynamicProvider>] int number = default,
                    [Default<StaticProvider>] string text = default)
                {
                    RenderCalls++;
                    Child(number, text);
                }

                [Composable]
                public static void Child(int number, string text)
                {
                    ChildCalls++;
                    Seen = number + ":" + text;
                }

                public static int Run()
                {
                    _ = Dynamic.Value;
                    _ = Static.Value;
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Root(ctx, changed, defaults));
                    if (Seen != "1:first" || RenderCalls != 1 || ChildCalls != 1) return 1;

                    Dynamic.Value = 2;
                    if (!composition.Recompose()) return 2;
                    composition.ApplyChanges();
                    if (Seen != "2:first" || RenderCalls != 2 || ChildCalls != 2) return 3;

                    Static.Value = "second";
                    if (!composition.Recompose()) return 4;
                    composition.ApplyChanges();
                    if (Seen != "2:second" || RenderCalls != 3 || ChildCalls != 3) return 5;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void MultipleDefaultsKeepRememberSlotsAcrossMaskChanges()
    {
        const string source = """
            using System;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public partial class FirstProvider : IDefaultValueProvider
            {
                public static int Calls;
                [Composable(ComposableMode.Inline)]
                public static object Create()
                {
                    Calls++;
                    return Composables.Remember("first", () => new object());
                }
            }

            public partial class SecondProvider : IDefaultValueProvider
            {
                public static int Calls;
                [Composable(ComposableMode.Inline)]
                public static object Create()
                {
                    Calls++;
                    _ = Example.Dependency.Value;
                    return Composables.Remember("second", () => new object());
                }
            }

            public static partial class Example
            {
                public static readonly SnapshotMutableState<int> Trigger = Composables.CreateMutableState(1);
                public static readonly SnapshotMutableState<int> Dependency = Composables.CreateMutableState(1);
                public static readonly SnapshotMutableState<bool> OmitFirst = Composables.CreateMutableState(true);
                public static readonly object Explicit = new object();
                public static object SeenFirst;
                public static object SeenSecond;

                [Composable]
                public static void Render(int trigger,
                    [Default<FirstProvider>] object first = default,
                    [Default<SecondProvider>] object second = default)
                {
                    SeenFirst = first;
                    SeenSecond = second;
                }

                public static int Run()
                {
                    _ = Trigger.Value;
                    _ = Dependency.Value;
                    _ = OmitFirst.Value;
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) =>
                    {
                        bool omit = OmitFirst.Value;
                        Builders.Render(Trigger.Value, omit ? default : Explicit, default, ctx, default,
                            new ComposableArgumentsDefaultState(new byte[] { omit ? (byte)1 : (byte)0, 1 }));
                    });
                    object first = SeenFirst;
                    object second = SeenSecond;
                    if (first == null || second == null || FirstProvider.Calls != 1 || SecondProvider.Calls != 1) return 1;

                    Trigger.Value = 2;
                    if (!composition.Recompose()) return 2;
                    composition.ApplyChanges();
                    if (!ReferenceEquals(first, SeenFirst) || !ReferenceEquals(second, SeenSecond) ||
                        FirstProvider.Calls != 1 || SecondProvider.Calls != 1) return 3;

                    OmitFirst.Value = false;
                    if (!composition.Recompose()) return 4;
                    composition.ApplyChanges();
                    if (!ReferenceEquals(Explicit, SeenFirst) || !ReferenceEquals(second, SeenSecond) ||
                        FirstProvider.Calls != 1 || SecondProvider.Calls != 2) return 5;

                    OmitFirst.Value = true;
                    if (!composition.Recompose()) return 6;
                    composition.ApplyChanges();
                    object replacement = SeenFirst;
                    if (ReferenceEquals(first, replacement) || !ReferenceEquals(second, SeenSecond) ||
                        FirstProvider.Calls != 2 || SecondProvider.Calls != 3) return 7;

                    Dependency.Value = 2;
                    if (!composition.Recompose()) return 8;
                    composition.ApplyChanges();
                    if (!ReferenceEquals(replacement, SeenFirst) || !ReferenceEquals(second, SeenSecond) ||
                        FirstProvider.Calls != 3 || SecondProvider.Calls != 4) return 9;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void OmittedArgumentsDifferFromExplicitDefaultAndNamedArguments()
    {
        const string source = """
            using System.Collections.Generic;
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public class FirstProvider : IDefaultValueProvider
            {
                public static int Calls;
                public static int Create() { Calls++; return 7; }
            }
            public class SecondProvider : IDefaultValueProvider
            {
                public static int Calls;
                public static int Create() { Calls++; return 9; }
            }
            public static partial class Example
            {
                public static readonly List<string> Values = new List<string>();
                public static int PlainSeen;

                [Composable]
                public static void Plain(int value = 5) { PlainSeen = value; }

                [Composable]
                public static void Render(
                    [Default<FirstProvider>] int first = default,
                    [Default<SecondProvider>] int second = default)
                {
                    Values.Add(first + ":" + second);
                }

                [Composable]
                public static void Root()
                {
                    Render();
                    Render(default);
                    Render(second: 4);
                    Render(second: default, first: default);
                    Plain();
                }

                public static int Run()
                {
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Root(ctx, changed, defaults));
                    if (Values.Count != 4) return 1;
                    if (Values[0] != "7:9" || Values[1] != "0:9" ||
                        Values[2] != "7:4" || Values[3] != "0:0") return 2;
                    if (FirstProvider.Calls != 2 || SecondProvider.Calls != 2) return 3;
                    if (PlainSeen != 5) return 4;
                    return 0;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void InlineComposableDefaultForwardsChangedValueWithoutOwnRestartScope()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public class Provider : IDefaultValueProvider
            {
                public static SnapshotMutableState<int> State = Composables.CreateMutableState(1);
                public static int Create() { return State.Value; }
            }

            public static partial class Example
            {
                public static int ChildCalls;
                public static int Seen;

                [Composable]
                public static void Root() { InlineRender(); }

                [Composable(ComposableMode.Inline)]
                public static void InlineRender([Default<Provider>] int value = default)
                {
                    Child(value);
                }

                [Composable]
                public static void Child(int value)
                {
                    ChildCalls++;
                    Seen = value;
                }

                public static int Run()
                {
                    _ = Provider.State.Value;
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Root(ctx, changed, defaults));
                    if (Seen != 1 || ChildCalls != 1) return 1;
                    Provider.State.Value = 2;
                    if (!composition.Recompose()) return 2;
                    composition.ApplyChanges();
                    return Seen == 2 && ChildCalls == 2 ? 0 : 3;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData(nameof(ComposableMode.Restartable), 1, 1)]
    [InlineData(nameof(ComposableMode.NonSkippable), 2, 1)]
    [InlineData(nameof(ComposableMode.Inline), 2, 2)]
    [InlineData(nameof(ComposableMode.NonRestartable), 2, 2)]
    [InlineData(nameof(ComposableMode.ReadOnly), 2, 2)]
    public void DefaultProviderFollowsMethodMode(
        string mode, int expectedRenderCalls, int expectedProviderCalls)
    {
        string source = $$"""
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public class Provider : IDefaultValueProvider
            {
                public static int Calls;
                public static int Create() { Calls++; return 7; }
            }

            public static partial class Example
            {
                public static SnapshotMutableState<int> Trigger = Composables.CreateMutableState(1);
                public static int RenderCalls;
                public static int Seen;

                [Composable]
                public static void Root() { _ = Trigger.Value; Render(); }

                [Composable(ComposableMode.{{mode}})]
                public static void Render([Default<Provider>] int value = default)
                {
                    RenderCalls++;
                    Child(value);
                }

                [Composable(ComposableMode.ReadOnly)]
                public static void Child(int value) { Seen = value; }

                public static int Run()
                {
                    _ = Trigger.Value;
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Root(ctx, changed, defaults));
                    if (RenderCalls != 1 || Provider.Calls != 1 || Seen != 7) return 1;
                    Trigger.Value = 2;
                    if (!composition.Recompose()) return 2;
                    composition.ApplyChanges();
                    return RenderCalls == {{expectedRenderCalls}} && Provider.Calls == {{expectedProviderCalls}} && Seen == 7 ? 0 : 3;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void UnstableRestartableRunsBodyButReusesDefault()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.Runtime.Snapshots;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public class Provider : IDefaultValueProvider
            {
                public static int Calls;
                public static int Create() { Calls++; return 7; }
            }

            public static partial class Example
            {
                public static SnapshotMutableState<int> Trigger = Composables.CreateMutableState(1);
                public static int RenderCalls;

                [Composable]
                public static void Root() { Render((object)Trigger.Value); }

                [Composable]
                public static void Render(object unstable, [Default<Provider>] int value = default)
                {
                    RenderCalls++;
                }

                public static int Run()
                {
                    _ = Trigger.Value;
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Root(ctx, changed, defaults));
                    Trigger.Value = 2;
                    if (!composition.Recompose()) return 1;
                    composition.ApplyChanges();
                    return RenderCalls == 2 && Provider.Calls == 1 ? 0 : 2;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void TwoDefaultsOwnOneMaskSlotAndTwoValueSlots()
    {
        const string source = """
            using DotNetCompose.Runtime;
            using DotNetCompose.Runtime.Composer;
            using DotNetCompose.SourceGenerators.Tests;

            namespace Integration;

            public class FirstProvider : IDefaultValueProvider
            {
                public static int Create() => 1;
            }
            public class SecondProvider : IDefaultValueProvider
            {
                public static int Create() => 2;
            }

            public static partial class Example
            {
                [Composable]
                public static void Render(
                    [Default<FirstProvider>] int first = default,
                    [Default<SecondProvider>] int second = default)
                {
                    Child(first, second);
                }

                [Composable]
                public static void Child(int first, int second) { }

                public static int Run()
                {
                    using var composition = new Composition<object>(new GeneratorRuntimeTests.Applier());
                    composition.SetContent((ctx, changed, defaults) => Builders.Render(
                        default, default, ctx, default,
                        new ComposableArgumentsDefaultState(new byte[] { 1, 1 })));
                    using var reader = composition.SlotTable.OpenReader();
                    return reader.GetSlotSize(1) == 3 && reader.GetSlotSize(2) == 1 ? 0 : 1;
                }
            }
            """;

        Type example = CompileExample(source);
        Assert.Equal(0, (int)example.GetMethod("Run")!.Invoke(null, null)!);
    }

    private static Type CompileExample(string source, bool? generateDiagnostics = null)
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
        GeneratorDriver driver = GeneratorTestHelper.CreateGeneratorDriver(
            generateDiagnostics: generateDiagnostics);
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
