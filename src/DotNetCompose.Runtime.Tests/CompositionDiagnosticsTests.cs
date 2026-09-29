using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Reflection;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Diagnostics;

namespace DotNetCompose.Runtime.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticsCollection
{
    public const string Name = "Composition diagnostics";
}

[Collection(DiagnosticsCollection.Name)]
public sealed class CompositionDiagnosticsTests
{
    [Fact]
    public void IsSupportedIsPublishedAsTheDiagnosticsFeatureSwitch()
    {
        PropertyInfo property = typeof(CompositionDiagnosticsRuntime)
            .GetProperty(nameof(CompositionDiagnosticsRuntime.IsSupported))!;
        FeatureSwitchDefinitionAttribute attribute = Assert.Single(
            property.GetCustomAttributes<FeatureSwitchDefinitionAttribute>());

        Assert.Equal("DotNetCompose.Diagnostics.IsSupported", attribute.SwitchName);
    }

    [Fact]
    public void SessionsReferenceCountTheirRuntimeFlags()
    {
        using Composition<object> composition = new Composition<object>(new NoOpApplier());
        CompositionDiagnosticsSession composables = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables });
        CompositionDiagnosticsSession passes = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Passes });

        Assert.True(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Composables));
        Assert.True(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Passes));

        composables.Dispose();
        Assert.False(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Composables));
        Assert.True(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Passes));

        passes.Dispose();
        Assert.False(CompositionDiagnosticsRuntime.IsEnabled(
            CompositionDiagnosticsFlags.Composables | CompositionDiagnosticsFlags.Passes));
    }

    [Fact]
    public void BeginWithoutAnActivePassAllocatesNoPayload()
    {
        for (int index = 0; index < 1_000; index++)
            _ = CompositionDiagnosticsRuntime.Begin(41, 501, "Disabled", "Disabled.cs", 1, "value");

        long before = GC.GetAllocatedBytesForCurrentThread();
        CompositionDiagnosticsToken token = default;
        for (int index = 0; index < 10_000; index++)
            token = CompositionDiagnosticsRuntime.Begin(41, 501, "Disabled", "Disabled.cs", 1, "value");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(token.IsActive);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void EventSourceListenerDynamicallyEnablesAndDisablesTracing()
    {
        using CompositionEventListener listener = new CompositionEventListener();
        using Composition<object> providerOwner = new Composition<object>(new NoOpApplier());
        bool enabled = SpinWait.SpinUntil(
            () => (CompositionDiagnosticsRuntime.EnabledFlags &
                (int)CompositionDiagnosticsFlags.Passes) != 0,
            TimeSpan.FromSeconds(2));
        Assert.True(enabled,
            $"ProviderFound={listener.ProviderFound}; Flags={CompositionDiagnosticsRuntime.EnabledFlags}; " +
            $"Events={string.Join(",", listener.EventNames)}; Messages={string.Join(" | ", listener.Messages)}");

        providerOwner.SetContent((_, _, _) =>
        {
        });

        Assert.Contains("CompositionPassStarted", listener.EventNames);
        Assert.Contains("CompositionPassEnded", listener.EventNames);

        listener.Stop();
        Assert.True(SpinWait.SpinUntil(
            () => (CompositionDiagnosticsRuntime.EnabledFlags &
                (int)CompositionDiagnosticsFlags.Passes) == 0,
            TimeSpan.FromSeconds(2)));
        int eventCount = listener.EventNames.Count;

        using (Composition<object> composition = new Composition<object>(new NoOpApplier()))
            composition.SetContent((_, _, _) =>
            {
            });

        Assert.Equal(eventCount, listener.EventNames.Count);
    }

    [Fact]
    public void DisablingOneEventSourceListenerKeepsFlagsRequiredByAnotherListener()
    {
        using Composition<object> providerOwner = new Composition<object>(new NoOpApplier());
        using CompositionEventListener first = new CompositionEventListener();
        using CompositionEventListener second = new CompositionEventListener();

        Assert.True(SpinWait.SpinUntil(
            () => CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Passes),
            TimeSpan.FromSeconds(2)));

        first.Stop();
        Assert.True(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Passes));

        second.Stop();
        Assert.True(SpinWait.SpinUntil(
            () => !CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Passes),
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void LateEventSourceListenerReceivesDefinitionsAndAbortedFrameEnd()
    {
        using Composition<object> composition = new Composition<object>(new NoOpApplier());
        using (CompositionDiagnosticsSession session = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables }))
        {
            CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: false);
            CompositionDiagnosticsToken token = CompositionDiagnosticsRuntime.Begin(
                31, 401, "Late", "Late.cs", 12, "value");
            CompositionDiagnosticsRuntime.End(
                token,
                ComposableExecutionOutcome.Executed,
                forced: false,
                stackalloc byte[] { (byte)CompositionParameterState.Static });
            CompositionDiagnosticsRuntime.CompleteCompositionPass(composition);
        }

        using CompositionEventListener listener = new CompositionEventListener();
        Assert.True(SpinWait.SpinUntil(
            () => CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Composables),
            TimeSpan.FromSeconds(2)));

        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: true);
        CompositionDiagnosticsToken aborted = CompositionDiagnosticsRuntime.Begin(
            31, 401, "Late", "Late.cs", 12, "value");
        CompositionDiagnosticsRuntime.ReportCompositionPassException(
            composition,
            new InvalidOperationException("failed"));

        Assert.True(SpinWait.SpinUntil(
            () => listener.EventNames.Contains("CompositionPassFailed"),
            TimeSpan.FromSeconds(2)));
        Assert.Contains("MethodDefinition", listener.EventNames);
        Assert.Contains("ComposableStarted", listener.EventNames);
        Assert.Contains("ComposableEnded", listener.EventNames);
        Assert.Contains((int)ComposableExecutionOutcome.Aborted, listener.ComposableEndOutcomes);
        Assert.True(aborted.IsActive);
    }

    [Fact]
    public void PassFailureAbortsNestedFramesInReverseOrderAndPreservesLastSnapshot()
    {
        using Composition<object> composition = new Composition<object>(new NoOpApplier());
        using CompositionDiagnosticsSession session = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.All });
        RecordingObserver observer = new RecordingObserver();
        using IDisposable subscription = session.Subscribe(observer);

        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: false);
        CompositionDiagnosticsToken completed = CompositionDiagnosticsRuntime.Begin(
            1, 101, "Completed", "Diagnostics.cs", 10, "value");
        CompositionDiagnosticsRuntime.End(
            completed,
            ComposableExecutionOutcome.Executed,
            forced: false,
            stackalloc byte[] { (byte)CompositionParameterState.Static });
        CompositionDiagnosticsRuntime.CompleteCompositionPass(composition);
        CompositionDiagnosticsSnapshot completedSnapshot = session.CaptureSnapshot();

        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: true);
        CompositionDiagnosticsToken parent = CompositionDiagnosticsRuntime.Begin(
            2, 102, "Parent", "Diagnostics.cs", 20, string.Empty);
        CompositionDiagnosticsToken child = CompositionDiagnosticsRuntime.Begin(
            3, 103, "Child", "Diagnostics.cs", 30, string.Empty);
        InvalidOperationException failure = new InvalidOperationException("boom");
        CompositionDiagnosticsRuntime.ReportCompositionPassException(composition, failure);

        CompositionDiagnosticEvent[] aborted = observer.Events
            .Where(value => value.Kind == CompositionDiagnosticEventKind.ComposableEnded &&
                value.Outcome == ComposableExecutionOutcome.Aborted)
            .ToArray();
        Assert.Equal(new[] { child.InvocationId, parent.InvocationId },
            aborted.Select(value => value.InvocationId));
        CompositionDiagnosticEvent passFailure = Assert.Single(observer.Events,
            value => value.Kind == CompositionDiagnosticEventKind.CompositionPassFailed);
        Assert.Same(failure, passFailure.Exception);

        CompositionDiagnosticsSnapshot afterFailure = session.CaptureSnapshot();
        Assert.Same(completedSnapshot, afterFailure);
        Assert.Equal("Completed", Assert.Single(afterFailure.Composables).Source.MemberName);
    }

    [Fact]
    public void CompletedNestedFramesProduceAnInvocationTree()
    {
        using Composition<object> composition = new Composition<object>(new NoOpApplier());
        using CompositionDiagnosticsSession session = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions
            {
                Flags = CompositionDiagnosticsFlags.Composables |
                    CompositionDiagnosticsFlags.ParameterStates
            });

        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: false);
        CompositionDiagnosticsToken parent = CompositionDiagnosticsRuntime.Begin(
            51, 601, "Parent", "Tree.cs", 10, "value");
        CompositionDiagnosticsToken child = CompositionDiagnosticsRuntime.Begin(
            52, 602, "Child", "Tree.cs", 20, "value");
        CompositionDiagnosticsRuntime.End(
            child,
            ComposableExecutionOutcome.Skipped,
            forced: false,
            stackalloc byte[] { (byte)CompositionParameterState.Same });
        CompositionDiagnosticsRuntime.End(
            parent,
            ComposableExecutionOutcome.Executed,
            forced: true,
            stackalloc byte[] { (byte)CompositionParameterState.Different });
        CompositionDiagnosticsRuntime.CompleteCompositionPass(composition);

        ComposableInvocationSnapshot parentSnapshot = Assert.Single(
            session.CaptureSnapshot().Composables);
        ComposableInvocationSnapshot childSnapshot = Assert.Single(parentSnapshot.Children);
        Assert.Equal(parentSnapshot.InvocationId, childSnapshot.ParentInvocationId);
        Assert.Equal(ComposableExecutionOutcome.Executed, parentSnapshot.Outcome);
        Assert.Equal(ComposableExecutionOutcome.Skipped, childSnapshot.Outcome);
        Assert.True(parentSnapshot.Forced);
        Assert.False(childSnapshot.Forced);
        Assert.Equal(CompositionParameterState.Different, Assert.Single(parentSnapshot.Parameters).State);
        Assert.Equal(CompositionParameterState.Same, Assert.Single(childSnapshot.Parameters).State);
    }

    [Fact]
    public void TokenKeepsItsFramePairingWhenFlagsAreDisabledAfterBegin()
    {
        using Composition<object> composition = new Composition<object>(new NoOpApplier());
        CompositionDiagnosticsSession session = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables });

        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: false);
        CompositionDiagnosticsToken token = CompositionDiagnosticsRuntime.Begin(
            11, 201, "Open", "Diagnostics.cs", 40, string.Empty);
        Assert.True(token.IsActive);

        session.Dispose();
        Assert.False(CompositionDiagnosticsRuntime.IsEnabled(CompositionDiagnosticsFlags.Composables));
        CompositionDiagnosticsRuntime.End(
            token,
            ComposableExecutionOutcome.Executed,
            forced: false,
            ReadOnlySpan<byte>.Empty);
        CompositionDiagnosticsRuntime.CompleteCompositionPass(composition);

        using CompositionDiagnosticsSession next = composition.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables });
        CompositionDiagnosticsRuntime.BeginCompositionPass(composition, recompose: true);
        CompositionDiagnosticsToken nextToken = CompositionDiagnosticsRuntime.Begin(
            12, 202, "Next", "Diagnostics.cs", 50, string.Empty);
        CompositionDiagnosticsRuntime.End(
            nextToken,
            ComposableExecutionOutcome.Skipped,
            forced: false,
            ReadOnlySpan<byte>.Empty);
        CompositionDiagnosticsRuntime.CompleteCompositionPass(composition);

        ComposableInvocationSnapshot invocation = Assert.Single(next.CaptureSnapshot().Composables);
        Assert.Equal("Next", invocation.Source.MemberName);
        Assert.Equal(ComposableExecutionOutcome.Skipped, invocation.Outcome);
    }

    [Fact]
    public void NestedPassesFromDifferentCompositionsKeepSeparateStacksAndSnapshots()
    {
        using Composition<object> outer = new Composition<object>(new NoOpApplier());
        using Composition<object> inner = new Composition<object>(new NoOpApplier());
        using CompositionDiagnosticsSession outerSession = outer.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables });
        using CompositionDiagnosticsSession innerSession = inner.StartDiagnostics(
            new CompositionDiagnosticsOptions { Flags = CompositionDiagnosticsFlags.Composables });

        CompositionDiagnosticsRuntime.BeginCompositionPass(outer, recompose: false);
        CompositionDiagnosticsToken outerToken = CompositionDiagnosticsRuntime.Begin(
            21, 301, "Outer", "Nested.cs", 10, string.Empty);

        CompositionDiagnosticsRuntime.BeginCompositionPass(inner, recompose: false);
        CompositionDiagnosticsToken innerToken = CompositionDiagnosticsRuntime.Begin(
            22, 302, "Inner", "Nested.cs", 20, string.Empty);
        CompositionDiagnosticsRuntime.End(
            innerToken,
            ComposableExecutionOutcome.Executed,
            forced: false,
            ReadOnlySpan<byte>.Empty);
        CompositionDiagnosticsRuntime.CompleteCompositionPass(inner);

        CompositionDiagnosticsRuntime.End(
            outerToken,
            ComposableExecutionOutcome.Executed,
            forced: false,
            ReadOnlySpan<byte>.Empty);
        CompositionDiagnosticsRuntime.CompleteCompositionPass(outer);

        Assert.Equal("Outer", Assert.Single(outerSession.CaptureSnapshot().Composables).Source.MemberName);
        Assert.Equal("Inner", Assert.Single(innerSession.CaptureSnapshot().Composables).Source.MemberName);
        Assert.NotEqual(
            outerSession.CaptureSnapshot().CompositionId,
            innerSession.CaptureSnapshot().CompositionId);
    }

    private sealed class RecordingObserver : ICompositionObserver
    {
        internal List<CompositionDiagnosticEvent> Events { get; } = new List<CompositionDiagnosticEvent>();
        public void OnEvent(CompositionDiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private sealed class CompositionEventListener : EventListener
    {
        private EventSource? _source;
        internal ConcurrentQueue<string> EventNames { get; } = new ConcurrentQueue<string>();
        internal ConcurrentQueue<string> Messages { get; } = new ConcurrentQueue<string>();
        internal ConcurrentQueue<int> ComposableEndOutcomes { get; } = new ConcurrentQueue<int>();
        internal bool ProviderFound => _source != null;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name != "DotNetCompose-Composition")
                return;
            _source = eventSource;
            EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x7f);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName != null)
                EventNames.Enqueue(eventData.EventName);
            if (eventData.EventName == "ComposableEnded" && eventData.Payload?.Count > 3)
                ComposableEndOutcomes.Enqueue(Convert.ToInt32(eventData.Payload[3]));
            if (eventData.Message != null)
                Messages.Enqueue(eventData.Message);
            if (eventData.EventName == "EventSourceMessage" && eventData.Payload != null)
                foreach (object? value in eventData.Payload)
                    Messages.Enqueue(value?.ToString() ?? "<null>");
        }

        internal void Stop()
        {
            if (_source != null)
                DisableEvents(_source);
        }
    }

    private sealed class NoOpApplier : IApplier<object>
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
}
