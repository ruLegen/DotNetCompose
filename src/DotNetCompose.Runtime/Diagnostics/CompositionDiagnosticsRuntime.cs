using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using System.Threading;

#if NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif

namespace DotNetCompose.Runtime.Diagnostics
{
    public static class CompositionDiagnosticsRuntime
    {
        private const string FeatureSwitchName = "DotNetCompose.Diagnostics.IsSupported";
        private const CompositionDiagnosticsFlags ComposableFrameFlags =
            CompositionDiagnosticsFlags.Composables |
            CompositionDiagnosticsFlags.ParameterStates |
            CompositionDiagnosticsFlags.StateReads |
            CompositionDiagnosticsFlags.Timings |
            CompositionDiagnosticsFlags.DetailedTimeline;

        private static readonly object s_gate = new object();
        private static readonly int[] s_sessionFlagCounts = new int[7];
        private static readonly ConditionalWeakTable<object, CompositionState> s_compositions =
            new ConditionalWeakTable<object, CompositionState>();
        private static readonly ConcurrentDictionary<long, MethodDescriptor> s_methods =
            new ConcurrentDictionary<long, MethodDescriptor>();
        private static readonly bool s_isSupported = ReadIsSupported();
        private static int s_eventSourceFlags;
        private static int s_enabledFlags;
        private static long s_nextCompositionId;
        private static long s_nextPassId;
        private static long s_nextInvocationId;

        [ThreadStatic]
        private static List<PassFrame>? t_passStack;

        private static CompositionEventSource EventSource => CompositionEventSourceHolder.Instance;

        private static class CompositionEventSourceHolder
        {
            internal static readonly CompositionEventSource Instance = new CompositionEventSource();
        }

        internal static void EnsureEventSource() => _ = EventSource;

#if NET9_0_OR_GREATER
        [FeatureSwitchDefinition(FeatureSwitchName)]
#endif
        public static bool IsSupported => s_isSupported;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEnabled(CompositionDiagnosticsFlags flags) =>
            IsSupported && (Volatile.Read(ref s_enabledFlags) & (int)flags) != 0;

        internal static int EnabledFlags => Volatile.Read(ref s_enabledFlags);

        private static bool ReadIsSupported() =>
            !AppContext.TryGetSwitch(FeatureSwitchName, out bool enabled) || enabled;

        internal static CompositionDiagnosticsSession StartSession(
            object composition,
            CompositionDiagnosticsOptions? options)
        {
            if (!IsSupported)
                throw new InvalidOperationException("DotNetCompose diagnostics were disabled by the application feature switch.");
            EnsureEventSource();
            CompositionDiagnosticsOptions requested = options ?? new CompositionDiagnosticsOptions();
            CompositionDiagnosticsOptions copy = new CompositionDiagnosticsOptions { Flags = requested.Flags };
            CompositionState state = s_compositions.GetValue(composition, CreateCompositionState);
            return new CompositionDiagnosticsSession(state.Dispatcher, copy);
        }

        internal static CompositionDiagnosticsSnapshot GetSnapshot(object composition) =>
            s_compositions.TryGetValue(composition, out CompositionState? state)
                ? state.Dispatcher.CaptureSnapshot()
                : CompositionDiagnosticsSnapshot.Unavailable(
                    "Runtime diagnostics are not active. Call StartDiagnostics(), or attach an EventPipe listener for tracing.");

        private static CompositionState CreateCompositionState(object _) =>
            new CompositionState(Interlocked.Increment(ref s_nextCompositionId));

        internal static IDisposable RegisterSessionFlags(CompositionDiagnosticsFlags flags)
        {
            if (flags == CompositionDiagnosticsFlags.None) return EmptyRegistration.Instance;
            lock (s_gate)
            {
                for (int bit = 0; bit < s_sessionFlagCounts.Length; bit++)
                    if ((((int)flags) & (1 << bit)) != 0) s_sessionFlagCounts[bit]++;
                RecomputeEnabledFlagsLocked();
            }
            return new FlagRegistration(flags);
        }

        private static void SetEventSourceFlags(CompositionDiagnosticsFlags flags)
        {
            lock (s_gate)
            {
                s_eventSourceFlags = (int)flags;
                RecomputeEnabledFlagsLocked();
            }
        }

        private static void RecomputeEnabledFlagsLocked()
        {
            int flags = s_eventSourceFlags;
            for (int bit = 0; bit < s_sessionFlagCounts.Length; bit++)
                if (s_sessionFlagCounts[bit] > 0) flags |= 1 << bit;
            Volatile.Write(ref s_enabledFlags, flags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void BeginCompositionPass(object composition, bool recompose)
        {
            if (!IsEnabled(CompositionDiagnosticsFlags.All)) return;
            BeginCompositionPassCore(composition, recompose);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BeginCompositionPassCore(object composition, bool recompose)
        {
            try
            {
                CompositionDiagnosticsFlags eventFlags = (CompositionDiagnosticsFlags)Volatile.Read(ref s_eventSourceFlags);
                CompositionState? state;
                if (!s_compositions.TryGetValue(composition, out state))
                {
                    if (eventFlags == CompositionDiagnosticsFlags.None) return;
                    state = s_compositions.GetValue(composition, CreateCompositionState);
                }

                CompositionDiagnosticsFlags flags = state.Dispatcher.EnabledFlags | eventFlags;
                if (flags == CompositionDiagnosticsFlags.None) return;

                PassFrame pass = new PassFrame(
                    state,
                    Interlocked.Increment(ref s_nextPassId),
                    flags,
                    recompose);
                (t_passStack ??= new List<PassFrame>()).Add(pass);
                PublishPassStarted(pass);
            }
            catch
            {
                // Diagnostics must never alter composition behavior.
            }
        }

        internal static void CompleteCompositionPass(object composition)
        {
            PassFrame? pass = FindCurrentPass(composition);
            if (pass == null) return;
            try
            {
                AbortOpenInvocations(pass);
                long duration = pass.StartedTimestamp == 0
                    ? 0
                    : Stopwatch.GetTimestamp() - pass.StartedTimestamp;
                CompositionDiagnosticsFlags flags = CurrentFlags(pass.State);
                pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.CompositionPassEnded,
                    CompositionDiagnosticsFlags.Passes | CompositionDiagnosticsFlags.Timings,
                    pass.State.CompositionId,
                    pass.PassId,
                    durationTicks: duration));
                if ((flags & (CompositionDiagnosticsFlags.Passes |
                    CompositionDiagnosticsFlags.Timings |
                    CompositionDiagnosticsFlags.DetailedTimeline)) != 0)
                    EventSource.CompositionPassEnded(pass.State.CompositionId, pass.PassId, duration);

                if (pass.State.Dispatcher.EnabledFlags != CompositionDiagnosticsFlags.None)
                    pass.State.Dispatcher.ReplaceSnapshot(CreateSnapshot(pass));
                pass.State.PendingPass = pass;
            }
            catch { }
            finally
            {
                RemoveCurrentPass(pass);
            }
        }

        internal static void ReportCompositionPassException(object composition, Exception exception)
        {
            PassFrame? pass = FindCurrentPass(composition);
            if (pass == null) return;
            try
            {
                AbortOpenInvocations(pass);
                pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.CompositionPassFailed,
                    CompositionDiagnosticsFlags.Passes,
                    pass.State.CompositionId,
                    pass.PassId,
                    exception: exception));
                CompositionDiagnosticsFlags flags = CurrentFlags(pass.State);
                if ((flags & (CompositionDiagnosticsFlags.Passes |
                    CompositionDiagnosticsFlags.DetailedTimeline)) != 0)
                    EventSource.CompositionPassFailed(
                        pass.State.CompositionId,
                        pass.PassId,
                        exception.GetType().FullName ?? exception.GetType().Name,
                        exception.Message);
                pass.State.PendingPass = null;
            }
            catch { }
            finally
            {
                RemoveCurrentPass(pass);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompositionDiagnosticsToken Begin(
            long methodId,
            int groupKey,
            string memberName,
            string sourcePath,
            int sourceLine,
            string parameterNames)
        {
            if (!IsSupported) return default;
            PassFrame? pass = CurrentPass;
            if (pass == null) return default;
            CompositionDiagnosticsFlags flags = CurrentFlags(pass.State);
            if ((flags & ComposableFrameFlags) == 0) return default;
            return BeginCore(pass, flags, methodId, groupKey, memberName, sourcePath, sourceLine, parameterNames);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static CompositionDiagnosticsToken BeginCore(
            PassFrame pass,
            CompositionDiagnosticsFlags flags,
            long methodId,
            int groupKey,
            string memberName,
            string sourcePath,
            int sourceLine,
            string parameterNames)
        {
            try
            {
                MethodDescriptor descriptor = GetMethodDescriptor(
                    methodId, memberName, sourcePath, sourceLine, parameterNames);
                long parentId = pass.OpenInvocations.Count == 0
                    ? 0
                    : pass.OpenInvocations[pass.OpenInvocations.Count - 1].InvocationId;
                InvocationFrame frame = new InvocationFrame(
                    Interlocked.Increment(ref s_nextInvocationId),
                    parentId,
                    descriptor,
                    groupKey,
                    flags);
                if (pass.OpenInvocations.Count == 0)
                    pass.Roots.Add(frame);
                else
                    pass.OpenInvocations[pass.OpenInvocations.Count - 1].Children.Add(frame);
                pass.OpenInvocations.Add(frame);

                pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.ComposableStarted,
                    CompositionDiagnosticsFlags.Composables,
                    pass.State.CompositionId,
                    pass.PassId,
                    frame.InvocationId,
                    frame.ParentInvocationId,
                    methodId,
                    groupKey,
                    source: descriptor.Source));
                if ((flags & (CompositionDiagnosticsFlags.Composables |
                    CompositionDiagnosticsFlags.Timings |
                    CompositionDiagnosticsFlags.DetailedTimeline)) != 0)
                {
                    EventSource.EnsureMethodDefinition(descriptor);
                    EventSource.ComposableStarted(
                        pass.State.CompositionId,
                        pass.PassId,
                        frame.InvocationId,
                        frame.ParentInvocationId,
                        methodId,
                        groupKey);
                }
                return new CompositionDiagnosticsToken(pass.PassId, frame.InvocationId);
            }
            catch
            {
                return default;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void End(
            CompositionDiagnosticsToken token,
            ComposableExecutionOutcome outcome,
            bool forced,
            ReadOnlySpan<byte> parameterStates)
        {
            if (!token.IsActive) return;
            try
            {
                PassFrame? pass = CurrentPass;
                if (pass == null || pass.PassId != token.PassId) return;
                int index = pass.OpenInvocations.Count - 1;
                while (index >= 0 && pass.OpenInvocations[index].InvocationId != token.InvocationId) index--;
                if (index < 0) return;
                while (pass.OpenInvocations.Count - 1 > index)
                    CompleteInvocation(pass, pass.OpenInvocations[pass.OpenInvocations.Count - 1],
                        ComposableExecutionOutcome.Aborted, false, ReadOnlySpan<byte>.Empty);
                CompleteInvocation(pass, pass.OpenInvocations[index], outcome, forced, parameterStates);
            }
            catch
            {
                // Diagnostics must never alter composition behavior.
            }
        }

        internal static void RecordStateRead(object state)
        {
            try
            {
                PassFrame? pass = CurrentPass;
                if (pass == null || (CurrentFlags(pass.State) & CompositionDiagnosticsFlags.StateReads) == 0) return;
                InvocationFrame? invocation = pass.OpenInvocations.Count == 0
                    ? null
                    : pass.OpenInvocations[pass.OpenInvocations.Count - 1];
                if (invocation != null) invocation.StateReadCount++;
                int stateId = RuntimeHelpers.GetHashCode(state);
                pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.StateRead,
                    CompositionDiagnosticsFlags.StateReads,
                    pass.State.CompositionId,
                    pass.PassId,
                    invocation?.InvocationId ?? 0,
                    invocation?.ParentInvocationId ?? 0,
                    invocation?.Descriptor.MethodId ?? 0,
                    invocation?.GroupKey ?? 0,
                    source: invocation?.Descriptor.Source,
                    stateId: stateId));
                EventSource.StateRead(
                    pass.State.CompositionId,
                    pass.PassId,
                    invocation?.InvocationId ?? 0,
                    invocation?.Descriptor.MethodId ?? 0,
                    stateId);
            }
            catch { }
        }

        internal static void BeginApplyChanges(object composition, int operationCount)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return;
            PassFrame? pass = state.PendingPass;
            if (pass == null) return;
            CompositionDiagnosticsFlags flags = CurrentFlags(state);
            if ((flags & (CompositionDiagnosticsFlags.ApplyChanges |
                CompositionDiagnosticsFlags.Timings |
                CompositionDiagnosticsFlags.DetailedTimeline)) == 0) return;
            pass.ApplyActive = true;
            pass.ApplyStartedTimestamp = (flags & CompositionDiagnosticsFlags.Timings) != 0
                ? Stopwatch.GetTimestamp()
                : 0;
            state.Dispatcher.Publish(new CompositionDiagnosticEvent(
                CompositionDiagnosticEventKind.ApplyChangesStarted,
                CompositionDiagnosticsFlags.ApplyChanges,
                state.CompositionId,
                pass.PassId,
                operationCount: operationCount));
            EventSource.ApplyChangesStarted(state.CompositionId, pass.PassId, operationCount);
        }

        internal static void CompleteApplyChanges(object composition, int operationCount)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return;
            PassFrame? pass = state.PendingPass;
            if (pass == null) return;
            try
            {
                if (pass.ApplyActive)
                {
                    long duration = pass.ApplyStartedTimestamp == 0
                        ? 0
                        : Stopwatch.GetTimestamp() - pass.ApplyStartedTimestamp;
                    state.Dispatcher.Publish(new CompositionDiagnosticEvent(
                        CompositionDiagnosticEventKind.ApplyChangesEnded,
                        CompositionDiagnosticsFlags.ApplyChanges | CompositionDiagnosticsFlags.Timings,
                        state.CompositionId,
                        pass.PassId,
                        durationTicks: duration,
                        operationCount: operationCount));
                    EventSource.ApplyChangesEnded(state.CompositionId, pass.PassId, operationCount, duration);
                }
            }
            catch { }
            finally
            {
                state.PendingPass = null;
            }
        }

        internal static void ReportApplyChangesException(object composition, Exception exception)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return;
            PassFrame? pass = state.PendingPass;
            if (pass == null) return;
            try
            {
                state.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.ApplyChangesFailed,
                    CompositionDiagnosticsFlags.ApplyChanges,
                    state.CompositionId,
                    pass.PassId,
                    exception: exception));
                EventSource.ApplyChangesFailed(
                    state.CompositionId,
                    pass.PassId,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message);
            }
            catch { }
            finally
            {
                state.PendingPass = null;
            }
        }

        internal static void ReportChangesDiscarded(object composition, int operationCount)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return;
            PassFrame? pass = state.PendingPass;
            if (pass == null) return;
            try
            {
                CompositionDiagnosticsFlags flags = CurrentFlags(state);
                if ((flags & (CompositionDiagnosticsFlags.ApplyChanges |
                    CompositionDiagnosticsFlags.DetailedTimeline)) != 0)
                {
                    state.Dispatcher.Publish(new CompositionDiagnosticEvent(
                        CompositionDiagnosticEventKind.ChangesDiscarded,
                        CompositionDiagnosticsFlags.ApplyChanges,
                        state.CompositionId,
                        pass.PassId,
                        operationCount: operationCount));
                    EventSource.ChangesDiscarded(state.CompositionId, pass.PassId, operationCount);
                }
            }
            catch { }
            finally
            {
                state.PendingPass = null;
            }
        }

        internal static void ReportCompositionDisposed(object composition)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return;
            try
            {
                state.Dispatcher.Publish(new CompositionDiagnosticEvent(
                    CompositionDiagnosticEventKind.CompositionDisposed,
                    CompositionDiagnosticsFlags.Passes,
                    state.CompositionId,
                    state.PendingPass?.PassId ?? 0));
                state.Dispatcher.Dispose();
            }
            catch { }
            finally
            {
                s_compositions.Remove(composition);
            }
        }

        private static void PublishPassStarted(PassFrame pass)
        {
            pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                CompositionDiagnosticEventKind.CompositionPassStarted,
                CompositionDiagnosticsFlags.Passes,
                pass.State.CompositionId,
                pass.PassId));
            if ((pass.InitialFlags & (CompositionDiagnosticsFlags.Passes |
                CompositionDiagnosticsFlags.Timings |
                CompositionDiagnosticsFlags.DetailedTimeline)) != 0)
                EventSource.CompositionPassStarted(
                    pass.State.CompositionId,
                    pass.PassId,
                    pass.Recompose ? 1 : 0);
        }

        private static void CompleteInvocation(
            PassFrame pass,
            InvocationFrame frame,
            ComposableExecutionOutcome outcome,
            bool forced,
            ReadOnlySpan<byte> parameterStates)
        {
            int last = pass.OpenInvocations.Count - 1;
            if (last < 0 || !ReferenceEquals(pass.OpenInvocations[last], frame)) return;
            pass.OpenInvocations.RemoveAt(last);
            frame.Outcome = outcome;
            frame.Forced = forced;
            frame.DurationTicks = frame.StartedTimestamp == 0
                ? 0
                : Stopwatch.GetTimestamp() - frame.StartedTimestamp;
            if ((frame.Flags & CompositionDiagnosticsFlags.ParameterStates) != 0 && parameterStates.Length != 0)
                frame.Parameters = CreateParameters(frame.Descriptor.ParameterNames, parameterStates);

            pass.State.Dispatcher.Publish(new CompositionDiagnosticEvent(
                CompositionDiagnosticEventKind.ComposableEnded,
                CompositionDiagnosticsFlags.Composables |
                    CompositionDiagnosticsFlags.ParameterStates |
                    CompositionDiagnosticsFlags.Timings,
                pass.State.CompositionId,
                pass.PassId,
                frame.InvocationId,
                frame.ParentInvocationId,
                frame.Descriptor.MethodId,
                frame.GroupKey,
                outcome,
                frame.Descriptor.Source,
                frame.Parameters,
                frame.DurationTicks,
                forced: forced));

            if ((frame.Flags & (CompositionDiagnosticsFlags.Composables |
                CompositionDiagnosticsFlags.ParameterStates |
                CompositionDiagnosticsFlags.Timings |
                CompositionDiagnosticsFlags.DetailedTimeline)) == 0) return;

            ulong states0 = Pack(parameterStates, 0);
            ulong states1 = Pack(parameterStates, 32);
            EventSource.EnsureMethodDefinition(frame.Descriptor);
            EventSource.ComposableEnded(
                pass.State.CompositionId,
                pass.PassId,
                frame.InvocationId,
                (int)outcome,
                unchecked((long)states0),
                unchecked((long)states1),
                parameterStates.Length,
                forced ? 1 : 0,
                frame.DurationTicks);
            for (int offset = 64, chunk = 2; offset < parameterStates.Length; offset += 32, chunk++)
            {
                int count = Math.Min(32, parameterStates.Length - offset);
                EventSource.ParameterStatesChunk(
                    pass.State.CompositionId,
                    pass.PassId,
                    frame.InvocationId,
                    chunk,
                    unchecked((long)Pack(parameterStates, offset)),
                    count);
            }
        }

        private static void AbortOpenInvocations(PassFrame pass)
        {
            while (pass.OpenInvocations.Count != 0)
                CompleteInvocation(
                    pass,
                    pass.OpenInvocations[pass.OpenInvocations.Count - 1],
                    ComposableExecutionOutcome.Aborted,
                    false,
                    ReadOnlySpan<byte>.Empty);
        }

        private static CompositionDiagnosticsSnapshot CreateSnapshot(PassFrame pass)
        {
            ComposableInvocationSnapshot[] roots = new ComposableInvocationSnapshot[pass.Roots.Count];
            for (int index = 0; index < roots.Length; index++) roots[index] = Freeze(pass.Roots[index]);
            return new CompositionDiagnosticsSnapshot(
                true,
                roots.Length == 0
                    ? "The pass completed, but no generated composable diagnostics were recorded. Ensure DotNetComposeGenerateDiagnostics=true."
                    : "Idle",
                pass.State.CompositionId,
                pass.PassId,
                roots);
        }

        private static ComposableInvocationSnapshot Freeze(InvocationFrame frame)
        {
            ComposableInvocationSnapshot[] children = new ComposableInvocationSnapshot[frame.Children.Count];
            for (int index = 0; index < children.Length; index++) children[index] = Freeze(frame.Children[index]);
            return new ComposableInvocationSnapshot(
                frame.InvocationId,
                frame.ParentInvocationId,
                frame.Descriptor.MethodId,
                frame.GroupKey,
                frame.Descriptor.Source,
                frame.Outcome,
                frame.Forced,
                frame.Parameters,
                frame.StateReadCount,
                frame.DurationTicks,
                children);
        }

        private static PassFrame? CurrentPass
        {
            get
            {
                List<PassFrame>? stack = t_passStack;
                return stack == null || stack.Count == 0 ? null : stack[stack.Count - 1];
            }
        }

        private static PassFrame? FindCurrentPass(object composition)
        {
            if (!s_compositions.TryGetValue(composition, out CompositionState? state)) return null;
            PassFrame? pass = CurrentPass;
            return pass != null && ReferenceEquals(pass.State, state) ? pass : null;
        }

        private static void RemoveCurrentPass(PassFrame pass)
        {
            List<PassFrame>? stack = t_passStack;
            if (stack == null || stack.Count == 0 || !ReferenceEquals(stack[stack.Count - 1], pass)) return;
            stack.RemoveAt(stack.Count - 1);
            if (stack.Count == 0) t_passStack = null;
        }

        private static CompositionDiagnosticsFlags CurrentFlags(CompositionState state) =>
            state.Dispatcher.EnabledFlags |
            (CompositionDiagnosticsFlags)Volatile.Read(ref s_eventSourceFlags);

        private static IReadOnlyList<CompositionParameterInfo> CreateParameters(
            string[] parameterNames,
            ReadOnlySpan<byte> states)
        {
            CompositionParameterInfo[] result = new CompositionParameterInfo[states.Length];
            for (int index = 0; index < states.Length; index++)
            {
                string name = index < parameterNames.Length ? parameterNames[index] : $"#{index}";
                result[index] = new CompositionParameterInfo(index, name, (CompositionParameterState)states[index]);
            }
            return result;
        }

        private static ulong Pack(ReadOnlySpan<byte> states, int offset)
        {
            if (offset >= states.Length) return 0;
            ulong packed = 0;
            int count = Math.Min(32, states.Length - offset);
            for (int index = 0; index < count; index++)
                packed |= ((ulong)states[offset + index] & 0x3UL) << (index * 2);
            return packed;
        }

        private static MethodDescriptor GetMethodDescriptor(
            long methodId,
            string memberName,
            string sourcePath,
            int sourceLine,
            string parameterNames) =>
            s_methods.GetOrAdd(methodId, _ => new MethodDescriptor(
                methodId,
                new CompositionSourceLocation(sourcePath, sourceLine, memberName),
                string.IsNullOrEmpty(parameterNames)
                    ? Array.Empty<string>()
                    : parameterNames.Split(new[] { '\u001f' }, StringSplitOptions.None)));

        private sealed class CompositionState
        {
            internal CompositionState(long compositionId)
            {
                CompositionId = compositionId;
            }

            internal long CompositionId { get; }
            internal CompositionDiagnosticsDispatcher Dispatcher { get; } = new CompositionDiagnosticsDispatcher();
            internal PassFrame? PendingPass;
        }

        private sealed class PassFrame
        {
            internal PassFrame(
                CompositionState state,
                long passId,
                CompositionDiagnosticsFlags flags,
                bool recompose)
            {
                State = state;
                PassId = passId;
                InitialFlags = flags;
                Recompose = recompose;
                StartedTimestamp = (flags & CompositionDiagnosticsFlags.Timings) != 0
                    ? Stopwatch.GetTimestamp()
                    : 0;
            }

            internal CompositionState State { get; }
            internal long PassId { get; }
            internal CompositionDiagnosticsFlags InitialFlags { get; }
            internal bool Recompose { get; }
            internal long StartedTimestamp { get; }
            internal List<InvocationFrame> Roots { get; } = new List<InvocationFrame>();
            internal List<InvocationFrame> OpenInvocations { get; } = new List<InvocationFrame>();
            internal bool ApplyActive;
            internal long ApplyStartedTimestamp;
        }

        private sealed class InvocationFrame
        {
            internal InvocationFrame(
                long invocationId,
                long parentInvocationId,
                MethodDescriptor descriptor,
                int groupKey,
                CompositionDiagnosticsFlags flags)
            {
                InvocationId = invocationId;
                ParentInvocationId = parentInvocationId;
                Descriptor = descriptor;
                GroupKey = groupKey;
                Flags = flags;
                StartedTimestamp = (flags & CompositionDiagnosticsFlags.Timings) != 0
                    ? Stopwatch.GetTimestamp()
                    : 0;
            }

            internal long InvocationId { get; }
            internal long ParentInvocationId { get; }
            internal MethodDescriptor Descriptor { get; }
            internal int GroupKey { get; }
            internal CompositionDiagnosticsFlags Flags { get; }
            internal long StartedTimestamp { get; }
            internal List<InvocationFrame> Children { get; } = new List<InvocationFrame>();
            internal ComposableExecutionOutcome Outcome;
            internal IReadOnlyList<CompositionParameterInfo> Parameters = Array.Empty<CompositionParameterInfo>();
            internal bool Forced;
            internal int StateReadCount;
            internal long DurationTicks;
        }

        internal sealed class MethodDescriptor
        {
            internal MethodDescriptor(long methodId, CompositionSourceLocation source, string[] parameterNames)
            {
                MethodId = methodId;
                Source = source;
                ParameterNames = parameterNames;
            }

            internal long MethodId { get; }
            internal CompositionSourceLocation Source { get; }
            internal string[] ParameterNames { get; }
            internal int EventSourceEpoch;
        }

        private sealed class FlagRegistration : IDisposable
        {
            private CompositionDiagnosticsFlags _flags;
            internal FlagRegistration(CompositionDiagnosticsFlags flags) => _flags = flags;

            public void Dispose()
            {
                CompositionDiagnosticsFlags flags = _flags;
                if (flags == CompositionDiagnosticsFlags.None) return;
                _flags = CompositionDiagnosticsFlags.None;
                lock (s_gate)
                {
                    for (int bit = 0; bit < s_sessionFlagCounts.Length; bit++)
                        if ((((int)flags) & (1 << bit)) != 0) s_sessionFlagCounts[bit]--;
                    RecomputeEnabledFlagsLocked();
                }
            }
        }

        private sealed class EmptyRegistration : IDisposable
        {
            internal static readonly EmptyRegistration Instance = new EmptyRegistration();
            public void Dispose() { }
        }

        [EventSource(Name = "DotNetCompose-Composition")]
        private sealed class CompositionEventSource : EventSource
        {
            public static class Keywords
            {
                public const EventKeywords Passes = (EventKeywords)(1 << 0);
                public const EventKeywords Composables = (EventKeywords)(1 << 1);
                public const EventKeywords ParameterStates = (EventKeywords)(1 << 2);
                public const EventKeywords StateReads = (EventKeywords)(1 << 3);
                public const EventKeywords ApplyChanges = (EventKeywords)(1 << 4);
                public const EventKeywords Timings = (EventKeywords)(1 << 5);
                public const EventKeywords DetailedTimeline = (EventKeywords)(1 << 6);
            }

            private int _epoch;
            internal int Epoch => Volatile.Read(ref _epoch);

            protected override void OnEventCommand(EventCommandEventArgs command)
            {
                if (command.Command == EventCommand.Enable) Interlocked.Increment(ref _epoch);
                CompositionDiagnosticsFlags flags = CompositionDiagnosticsFlags.None;
                if (IsEnabled(EventLevel.Informational, Keywords.Passes)) flags |= CompositionDiagnosticsFlags.Passes;
                if (IsEnabled(EventLevel.Verbose, Keywords.Composables)) flags |= CompositionDiagnosticsFlags.Composables;
                if (IsEnabled(EventLevel.Verbose, Keywords.ParameterStates)) flags |= CompositionDiagnosticsFlags.ParameterStates;
                if (IsEnabled(EventLevel.Verbose, Keywords.StateReads)) flags |= CompositionDiagnosticsFlags.StateReads;
                if (IsEnabled(EventLevel.Informational, Keywords.ApplyChanges)) flags |= CompositionDiagnosticsFlags.ApplyChanges;
                if (IsEnabled(EventLevel.Verbose, Keywords.Timings)) flags |= CompositionDiagnosticsFlags.Timings;
                if (IsEnabled(EventLevel.Verbose, Keywords.DetailedTimeline)) flags |= CompositionDiagnosticsFlags.DetailedTimeline;
                SetEventSourceFlags(flags);
            }

            [NonEvent]
            internal void EnsureMethodDefinition(MethodDescriptor descriptor)
            {
                int epoch = Epoch;
                if (epoch == 0 || Volatile.Read(ref descriptor.EventSourceEpoch) == epoch) return;
                if (Interlocked.Exchange(ref descriptor.EventSourceEpoch, epoch) == epoch) return;
                MethodDefinition(
                    descriptor.MethodId,
                    descriptor.Source.MemberName,
                    descriptor.Source.FilePath,
                    descriptor.Source.Line,
                    string.Join("\u001f", descriptor.ParameterNames));
            }

            [Event(1, Level = EventLevel.Informational,
                Keywords = Keywords.Composables | Keywords.ParameterStates | Keywords.StateReads |
                    Keywords.Timings | Keywords.DetailedTimeline)]
            public void MethodDefinition(long methodId, string memberName, string sourcePath, int sourceLine, string parameterNames) =>
                WriteMethodDefinition(methodId, memberName, sourcePath, sourceLine, parameterNames);

            [Event(2, Level = EventLevel.Informational,
                Keywords = Keywords.Passes | Keywords.Timings | Keywords.DetailedTimeline)]
            public void CompositionPassStarted(long compositionId, long passId, long recompose) =>
                WriteLongs(2, stackalloc long[] { compositionId, passId, recompose });

            [Event(3, Level = EventLevel.Informational,
                Keywords = Keywords.Passes | Keywords.Timings | Keywords.DetailedTimeline)]
            public void CompositionPassEnded(long compositionId, long passId, long durationTicks) =>
                WriteLongs(3, stackalloc long[] { compositionId, passId, durationTicks });

            [Event(4, Level = EventLevel.Error,
                Keywords = Keywords.Passes | Keywords.DetailedTimeline)]
            public void CompositionPassFailed(long compositionId, long passId, string exceptionType, string message) =>
                WriteFailure(4, compositionId, passId, exceptionType, message);

            [Event(5, Level = EventLevel.Verbose,
                Keywords = Keywords.Composables | Keywords.Timings | Keywords.DetailedTimeline)]
            public void ComposableStarted(long compositionId, long passId, long invocationId,
                long parentInvocationId, long methodId, long groupKey) =>
                WriteLongs(5, stackalloc long[]
                {
                    compositionId, passId, invocationId, parentInvocationId, methodId, groupKey
                });

            [Event(6, Level = EventLevel.Verbose,
                Keywords = Keywords.Composables | Keywords.ParameterStates |
                    Keywords.Timings | Keywords.DetailedTimeline)]
            public void ComposableEnded(long compositionId, long passId, long invocationId,
                long outcome, long states0, long states1, long parameterCount, long forced, long durationTicks) =>
                WriteLongs(6, stackalloc long[]
                {
                    compositionId, passId, invocationId, outcome, states0, states1,
                    parameterCount, forced, durationTicks
                });

            [Event(7, Level = EventLevel.Verbose, Keywords = Keywords.ParameterStates)]
            public void ParameterStatesChunk(long compositionId, long passId, long invocationId,
                long chunkIndex, long packedStates, long parameterCount) =>
                WriteLongs(7, stackalloc long[]
                {
                    compositionId, passId, invocationId, chunkIndex, packedStates, parameterCount
                });

            [Event(8, Level = EventLevel.Verbose, Keywords = Keywords.StateReads)]
            public void StateRead(long compositionId, long passId, long invocationId, long methodId, long stateId) =>
                WriteLongs(8, stackalloc long[] { compositionId, passId, invocationId, methodId, stateId });

            [Event(9, Level = EventLevel.Informational,
                Keywords = Keywords.ApplyChanges | Keywords.Timings | Keywords.DetailedTimeline)]
            public void ApplyChangesStarted(long compositionId, long passId, long operationCount) =>
                WriteLongs(9, stackalloc long[] { compositionId, passId, operationCount });

            [Event(10, Level = EventLevel.Informational,
                Keywords = Keywords.ApplyChanges | Keywords.Timings | Keywords.DetailedTimeline)]
            public void ApplyChangesEnded(long compositionId, long passId, long operationCount, long durationTicks) =>
                WriteLongs(10, stackalloc long[] { compositionId, passId, operationCount, durationTicks });

            [Event(11, Level = EventLevel.Error,
                Keywords = Keywords.ApplyChanges | Keywords.DetailedTimeline)]
            public void ApplyChangesFailed(long compositionId, long passId, string exceptionType, string message) =>
                WriteFailure(11, compositionId, passId, exceptionType, message);

            [Event(12, Level = EventLevel.Informational,
                Keywords = Keywords.ApplyChanges | Keywords.DetailedTimeline)]
            public void ChangesDiscarded(long compositionId, long passId, long operationCount) =>
                WriteLongs(12, stackalloc long[] { compositionId, passId, operationCount });

            [NonEvent]
#if NET9_0_OR_GREATER
            [UnconditionalSuppressMessage("Trimming", "IL2026",
                Justification = "The payload consists exclusively of fixed-width Int64 primitives.")]
#endif
            private unsafe void WriteLongs(int eventId, ReadOnlySpan<long> values)
            {
                EventData* descriptors = stackalloc EventData[values.Length];
                fixed (long* valuesPointer = values)
                {
                    for (int index = 0; index < values.Length; index++)
                    {
                        descriptors[index].DataPointer = (IntPtr)(valuesPointer + index);
                        descriptors[index].Size = sizeof(long);
                    }
                    WriteEventCore(eventId, values.Length, descriptors);
                }
            }

            [NonEvent]
#if NET9_0_OR_GREATER
            [UnconditionalSuppressMessage("Trimming", "IL2026",
                Justification = "The EventData payload contains only Int64, Int32, and UTF-16 strings.")]
#endif
            private unsafe void WriteMethodDefinition(
                long methodId,
                string memberName,
                string sourcePath,
                int sourceLine,
                string parameterNames)
            {
                fixed (char* memberNamePointer = memberName)
                fixed (char* sourcePathPointer = sourcePath)
                fixed (char* parameterNamesPointer = parameterNames)
                {
                    EventData* descriptors = stackalloc EventData[5];
                    descriptors[0].DataPointer = (IntPtr)(&methodId);
                    descriptors[0].Size = sizeof(long);
                    descriptors[1].DataPointer = (IntPtr)memberNamePointer;
                    descriptors[1].Size = checked((memberName.Length + 1) * sizeof(char));
                    descriptors[2].DataPointer = (IntPtr)sourcePathPointer;
                    descriptors[2].Size = checked((sourcePath.Length + 1) * sizeof(char));
                    descriptors[3].DataPointer = (IntPtr)(&sourceLine);
                    descriptors[3].Size = sizeof(int);
                    descriptors[4].DataPointer = (IntPtr)parameterNamesPointer;
                    descriptors[4].Size = checked((parameterNames.Length + 1) * sizeof(char));
                    WriteEventCore(1, 5, descriptors);
                }
            }

            [NonEvent]
#if NET9_0_OR_GREATER
            [UnconditionalSuppressMessage("Trimming", "IL2026",
                Justification = "The EventData payload contains only Int64 and UTF-16 strings.")]
#endif
            private unsafe void WriteFailure(
                int eventId,
                long compositionId,
                long passId,
                string exceptionType,
                string message)
            {
                fixed (char* exceptionTypePointer = exceptionType)
                fixed (char* messagePointer = message)
                {
                    EventData* descriptors = stackalloc EventData[4];
                    descriptors[0].DataPointer = (IntPtr)(&compositionId);
                    descriptors[0].Size = sizeof(long);
                    descriptors[1].DataPointer = (IntPtr)(&passId);
                    descriptors[1].Size = sizeof(long);
                    descriptors[2].DataPointer = (IntPtr)exceptionTypePointer;
                    descriptors[2].Size = checked((exceptionType.Length + 1) * sizeof(char));
                    descriptors[3].DataPointer = (IntPtr)messagePointer;
                    descriptors[3].Size = checked((message.Length + 1) * sizeof(char));
                    WriteEventCore(eventId, 4, descriptors);
                }
            }
        }
    }
}
