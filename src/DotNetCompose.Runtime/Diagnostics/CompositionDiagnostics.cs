using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DotNetCompose.Runtime.Diagnostics
{
    [Flags]
    public enum CompositionDiagnosticsFlags
    {
        None = 0,
        Passes = 1 << 0,
        Composables = 1 << 1,
        ParameterStates = 1 << 2,
        StateReads = 1 << 3,
        ApplyChanges = 1 << 4,
        Timings = 1 << 5,
        DetailedTimeline = 1 << 6,
        All = Passes | Composables | ParameterStates | StateReads | ApplyChanges | Timings | DetailedTimeline
    }

    public enum CompositionDiagnosticEventKind
    {
        CompositionPassStarted,
        CompositionPassEnded,
        CompositionPassFailed,
        ComposableStarted,
        ComposableEnded,
        StateRead,
        ApplyChangesStarted,
        ApplyChangesEnded,
        ApplyChangesFailed,
        ChangesDiscarded,
        CompositionDisposed
    }

    public enum ComposableExecutionOutcome
    {
        Executed,
        Skipped,
        Aborted
    }

    public enum CompositionParameterState : byte
    {
        Uncertain = 0,
        Different = 1,
        Same = 2,
        Static = 3
    }

    /// <summary>An opaque handle pairing one generated diagnostics Begin call with its End call.</summary>
    public readonly struct CompositionDiagnosticsToken
    {
        internal CompositionDiagnosticsToken(long passId, long invocationId)
        {
            PassId = passId;
            InvocationId = invocationId;
        }

        internal long PassId { get; }
        internal long InvocationId { get; }
        public bool IsActive => PassId != 0 && InvocationId != 0;
    }

    [DebuggerDisplay("{MemberName,nq} ({FilePath,nq}:{Line})")]
    public sealed class CompositionSourceLocation
    {
        public CompositionSourceLocation(string filePath, int line, string memberName)
        {
            FilePath = filePath ?? string.Empty;
            Line = line;
            MemberName = memberName ?? string.Empty;
        }

        public string FilePath { get; }
        public int Line { get; }
        public string MemberName { get; }
    }

    [DebuggerDisplay("{Name,nq} = {State}")]
    public sealed class CompositionParameterInfo
    {
        public CompositionParameterInfo(int index, string name, CompositionParameterState state)
        {
            Index = index;
            Name = name ?? string.Empty;
            State = state;
        }

        public int Index { get; }
        public string Name { get; }
        public CompositionParameterState State { get; }
    }

    [DebuggerDisplay("{Kind}: pass {PassId}, invocation {InvocationId}")]
    public sealed class CompositionDiagnosticEvent
    {
        internal CompositionDiagnosticEvent(
            CompositionDiagnosticEventKind kind,
            CompositionDiagnosticsFlags category,
            long compositionId,
            long passId,
            long invocationId = 0,
            long parentInvocationId = 0,
            long methodId = 0,
            int groupKey = 0,
            ComposableExecutionOutcome outcome = ComposableExecutionOutcome.Executed,
            CompositionSourceLocation? source = null,
            IReadOnlyList<CompositionParameterInfo>? parameters = null,
            long durationTicks = 0,
            int stateId = 0,
            int operationCount = 0,
            Exception? exception = null,
            bool forced = false)
        {
            Kind = kind;
            Category = category;
            CompositionId = compositionId;
            PassId = passId;
            InvocationId = invocationId;
            ParentInvocationId = parentInvocationId;
            MethodId = methodId;
            GroupKey = groupKey;
            Outcome = outcome;
            Source = source;
            Parameters = parameters ?? Array.Empty<CompositionParameterInfo>();
            DurationTicks = durationTicks;
            StateId = stateId;
            OperationCount = operationCount;
            Exception = exception;
            Forced = forced;
        }

        public CompositionDiagnosticEventKind Kind { get; }
        public CompositionDiagnosticsFlags Category { get; }
        public long CompositionId { get; }
        public long PassId { get; }
        public long InvocationId { get; }
        public long ParentInvocationId { get; }
        public long MethodId { get; }
        public int GroupKey { get; }
        public ComposableExecutionOutcome Outcome { get; }
        public CompositionSourceLocation? Source { get; }
        public IReadOnlyList<CompositionParameterInfo> Parameters { get; }
        public long DurationTicks { get; }
        public int StateId { get; }
        public int OperationCount { get; }
        public Exception? Exception { get; }
        public bool Forced { get; }
    }

    public interface ICompositionObserver
    {
        void OnEvent(CompositionDiagnosticEvent diagnosticEvent);
    }

    public sealed class CompositionDiagnosticsOptions
    {
        public CompositionDiagnosticsFlags Flags { get; set; } =
            CompositionDiagnosticsFlags.Passes |
            CompositionDiagnosticsFlags.Composables |
            CompositionDiagnosticsFlags.ParameterStates |
            CompositionDiagnosticsFlags.StateReads |
            CompositionDiagnosticsFlags.ApplyChanges;
    }

    [DebuggerDisplay("Invocation {InvocationId}: {Source.MemberName,nq}, {Outcome}")]
    [DebuggerTypeProxy(typeof(ComposableInvocationSnapshotDebugView))]
    public sealed class ComposableInvocationSnapshot
    {
        internal ComposableInvocationSnapshot(
            long invocationId,
            long parentInvocationId,
            long methodId,
            int groupKey,
            CompositionSourceLocation source,
            ComposableExecutionOutcome outcome,
            bool forced,
            IReadOnlyList<CompositionParameterInfo> parameters,
            int stateReadCount,
            long durationTicks,
            IReadOnlyList<ComposableInvocationSnapshot> children)
        {
            InvocationId = invocationId;
            ParentInvocationId = parentInvocationId;
            MethodId = methodId;
            GroupKey = groupKey;
            Source = source;
            Outcome = outcome;
            Forced = forced;
            Parameters = parameters;
            StateReadCount = stateReadCount;
            DurationTicks = durationTicks;
            Children = children;
        }

        public long InvocationId { get; }
        public long ParentInvocationId { get; }
        public long MethodId { get; }
        public int GroupKey { get; }
        public CompositionSourceLocation Source { get; }
        public ComposableExecutionOutcome Outcome { get; }
        public bool Forced { get; }
        public IReadOnlyList<CompositionParameterInfo> Parameters { get; }
        public int StateReadCount { get; }
        public long DurationTicks { get; }
        public IReadOnlyList<ComposableInvocationSnapshot> Children { get; }
    }

    [DebuggerDisplay("Pass {PassId}, Available = {IsAvailable}, Composables = {Composables.Count}")]
    [DebuggerTypeProxy(typeof(CompositionDiagnosticsSnapshotDebugView))]
    public sealed class CompositionDiagnosticsSnapshot
    {
        internal CompositionDiagnosticsSnapshot(
            bool isAvailable,
            string status,
            long compositionId,
            long passId,
            IReadOnlyList<ComposableInvocationSnapshot> composables)
        {
            IsAvailable = isAvailable;
            Status = status;
            CompositionId = compositionId;
            PassId = passId;
            Composables = composables;
        }

        public bool IsAvailable { get; }
        public string Status { get; }
        public long CompositionId { get; }
        public long PassId { get; }
        public IReadOnlyList<ComposableInvocationSnapshot> Composables { get; }

        internal static CompositionDiagnosticsSnapshot Unavailable(string status) =>
            new CompositionDiagnosticsSnapshot(false, status, 0, 0, Array.Empty<ComposableInvocationSnapshot>());
    }

    public sealed class CompositionDiagnosticsErrorEventArgs : EventArgs
    {
        internal CompositionDiagnosticsErrorEventArgs(ICompositionObserver observer, Exception exception)
        {
            Observer = observer;
            Exception = exception;
        }

        public ICompositionObserver Observer { get; }
        public Exception Exception { get; }
    }

    internal sealed class CompositionDiagnosticsSnapshotDebugView
    {
        private readonly CompositionDiagnosticsSnapshot _snapshot;
        internal CompositionDiagnosticsSnapshotDebugView(CompositionDiagnosticsSnapshot snapshot) => _snapshot = snapshot;
        public string Status => _snapshot.Status;
        public long CompositionId => _snapshot.CompositionId;
        public long PassId => _snapshot.PassId;
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public IReadOnlyList<ComposableInvocationSnapshot> Composables => _snapshot.Composables;
    }

    internal sealed class ComposableInvocationSnapshotDebugView
    {
        private readonly ComposableInvocationSnapshot _invocation;
        internal ComposableInvocationSnapshotDebugView(ComposableInvocationSnapshot invocation) => _invocation = invocation;
        public long InvocationId => _invocation.InvocationId;
        public long MethodId => _invocation.MethodId;
        public int GroupKey => _invocation.GroupKey;
        public CompositionSourceLocation Source => _invocation.Source;
        public ComposableExecutionOutcome Outcome => _invocation.Outcome;
        public bool Forced => _invocation.Forced;
        public int StateReadCount => _invocation.StateReadCount;
        public long DurationTicks => _invocation.DurationTicks;
        public TimeSpan DurationTime => TimeSpan.FromTicks(DurationTicks);
        public IReadOnlyList<CompositionParameterInfo> Parameters => _invocation.Parameters;
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public IReadOnlyList<ComposableInvocationSnapshot> Children => _invocation.Children;
    }
}
