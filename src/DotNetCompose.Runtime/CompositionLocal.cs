using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime
{
    public abstract class CompositionLocal
    {
        private protected CompositionLocal() { }

        internal abstract CompositionLocalValueHolder DefaultValueHolder { get; }

        internal abstract CompositionLocalValueHolder UpdatedValueHolder(
            ProvidedValue value,
            CompositionLocalValueHolder? previous,
            out IStateObject? changedState);
    }

    public abstract partial class CompositionLocal<T> : CompositionLocal
    {
        private readonly LazyValueHolder<T> _defaultValueHolder;

        private protected CompositionLocal(Func<T> defaultFactory)
        {
            if (defaultFactory == null) throw new ArgumentNullException(nameof(defaultFactory));
            _defaultValueHolder = new LazyValueHolder<T>(defaultFactory);
        }

        [Composable(ComposableMode.ReadOnly)]
        public T Current()
        {
            IComposerContext context = Composables.CurrentContext()
                ?? throw new InvalidOperationException("CompositionLocal.Current can only be read during composition.");
            return context.Consume(this);
        }

        internal override CompositionLocalValueHolder DefaultValueHolder => _defaultValueHolder;

        internal T Read(CompositionLocalScope scope)
        {
            CompositionLocalValueHolder holder;
            if (!scope.TryGet(this, out CompositionLocalValueHolder? provided)) holder = _defaultValueHolder;
            else holder = provided!;
            return ((CompositionLocalValueHolder<T>)holder).ReadValue();
        }
    }

    public abstract class ProvidableCompositionLocal<T> : CompositionLocal<T>
    {
        private protected ProvidableCompositionLocal(Func<T> defaultFactory) : base(defaultFactory) { }

        public ProvidedValue<T> Provides(T value) => new ProvidedValue<T>(this, value, true);

        public ProvidedValue<T> ProvidesDefault(T value) => new ProvidedValue<T>(this, value, false);
    }

    public abstract class ProvidedValue
    {
        private protected ProvidedValue(CompositionLocal compositionLocal, bool canOverride)
        {
            CompositionLocal = compositionLocal;
            CanOverride = canOverride;
        }

        internal CompositionLocal CompositionLocal { get; }
        internal bool CanOverride { get; }
    }

    public sealed class ProvidedValue<T> : ProvidedValue
    {
        internal ProvidedValue(ProvidableCompositionLocal<T> compositionLocal, T value, bool canOverride)
            : base(compositionLocal, canOverride)
        {
            Value = value;
        }

        internal T Value { get; }
    }

    internal sealed class DynamicProvidableCompositionLocal<T> : ProvidableCompositionLocal<T>
    {
        private readonly ISnapshotMutationPolicy<T> _policy;

        internal DynamicProvidableCompositionLocal(Func<T> defaultFactory, ISnapshotMutationPolicy<T> policy)
            : base(defaultFactory)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        internal override CompositionLocalValueHolder UpdatedValueHolder(
            ProvidedValue value,
            CompositionLocalValueHolder? previous,
            out IStateObject? changedState)
        {
            T next = ((ProvidedValue<T>)value).Value;
            if (previous is DynamicValueHolder<T> dynamic)
            {
                bool changed = dynamic.State.SetValueAndReportChange(next);
                changedState = changed ? dynamic.State : null;
                return dynamic;
            }

            SnapshotMutableState<T> state = new SnapshotMutableState<T>(next, _policy);
            changedState = state;
            return new DynamicValueHolder<T>(state);
        }
    }

    internal sealed class StaticProvidableCompositionLocal<T> : ProvidableCompositionLocal<T>
    {
        internal StaticProvidableCompositionLocal(Func<T> defaultFactory) : base(defaultFactory) { }

        internal override CompositionLocalValueHolder UpdatedValueHolder(
            ProvidedValue value,
            CompositionLocalValueHolder? previous,
            out IStateObject? changedState)
        {
            changedState = null;
            T next = ((ProvidedValue<T>)value).Value;
            if (previous is StaticValueHolder<T> @static &&
                EqualityComparer<T>.Default.Equals(@static.Value, next))
                return @static;
            return new StaticValueHolder<T>(next);
        }
    }

    internal abstract class CompositionLocalValueHolder
    {
        internal abstract bool IsEquivalentTo(CompositionLocalValueHolder other);
    }

    internal abstract class CompositionLocalValueHolder<T> : CompositionLocalValueHolder
    {
        internal abstract T ReadValue();
    }

    internal sealed class LazyValueHolder<T> : CompositionLocalValueHolder<T>
    {
        private readonly object _gate = new object();
        private volatile Func<T>? _valueFactory;
        private T _value = default!;

        internal LazyValueHolder(Func<T> valueFactory)
        {
            _valueFactory = valueFactory;
        }

        internal override T ReadValue()
        {
            if (_valueFactory == null) return _value;
            lock (_gate)
            {
                if (_valueFactory == null) return _value;
                T value = _valueFactory();
                _value = value;
                _valueFactory = null;
                return value;
            }
        }
        internal override bool IsEquivalentTo(CompositionLocalValueHolder other) => ReferenceEquals(this, other);
    }

    internal sealed class StaticValueHolder<T> : CompositionLocalValueHolder<T>
    {
        internal StaticValueHolder(T value) { Value = value; }
        internal T Value { get; }
        internal override T ReadValue() => Value;
        internal override bool IsEquivalentTo(CompositionLocalValueHolder other)
            => other is StaticValueHolder<T> holder && EqualityComparer<T>.Default.Equals(Value, holder.Value);
    }

    internal sealed class DynamicValueHolder<T> : CompositionLocalValueHolder<T>
    {
        internal DynamicValueHolder(SnapshotMutableState<T> state) { State = state; }
        internal SnapshotMutableState<T> State { get; }
        internal override T ReadValue() => State.Value;
        internal override bool IsEquivalentTo(CompositionLocalValueHolder other)
            => other is DynamicValueHolder<T> holder && ReferenceEquals(State, holder.State);
    }

    internal sealed class CompositionLocalScope
    {
        private sealed class LocalComparer : IEqualityComparer<CompositionLocal>
        {
            internal static readonly LocalComparer Instance = new LocalComparer();
            public bool Equals(CompositionLocal? x, CompositionLocal? y) => ReferenceEquals(x, y);
            public int GetHashCode(CompositionLocal obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        internal static readonly CompositionLocalScope Empty = new CompositionLocalScope(
            new Dictionary<CompositionLocal, CompositionLocalValueHolder>(LocalComparer.Instance));

        private readonly Dictionary<CompositionLocal, CompositionLocalValueHolder> _values;

        private CompositionLocalScope(Dictionary<CompositionLocal, CompositionLocalValueHolder> values)
        {
            _values = values;
        }

        internal bool Contains(CompositionLocal local) => _values.ContainsKey(local);

        internal bool TryGet(CompositionLocal local, out CompositionLocalValueHolder? holder)
            => _values.TryGetValue(local, out holder);

        internal static CompositionLocalScope Merge(
            CompositionLocalScope parent,
            Dictionary<CompositionLocal, CompositionLocalValueHolder> values,
            CompositionLocalScope? previous)
        {
            Dictionary<CompositionLocal, CompositionLocalValueHolder> result =
                new Dictionary<CompositionLocal, CompositionLocalValueHolder>(parent._values, LocalComparer.Instance);
            foreach (KeyValuePair<CompositionLocal, CompositionLocalValueHolder> item in values)
                result[item.Key] = item.Value;

            if (previous != null && previous.HasSameEntries(result)) return previous;
            if (result.Count == 0) return Empty;
            return new CompositionLocalScope(result);
        }

        private bool HasSameEntries(Dictionary<CompositionLocal, CompositionLocalValueHolder> other)
        {
            if (_values.Count != other.Count) return false;
            foreach (KeyValuePair<CompositionLocal, CompositionLocalValueHolder> item in _values)
                if (!other.TryGetValue(item.Key, out CompositionLocalValueHolder? value) ||
                    !item.Value.IsEquivalentTo(value))
                    return false;
            return true;
        }

        internal static Dictionary<CompositionLocal, CompositionLocalValueHolder> CreateValueMap()
            => new Dictionary<CompositionLocal, CompositionLocalValueHolder>(LocalComparer.Instance);
    }

    internal sealed class CompositionLocalProviderState
    {
        internal CompositionLocalProviderState(
            Dictionary<CompositionLocal, CompositionLocalValueHolder> values,
            CompositionLocalScope scope)
        {
            Values = values;
            Scope = scope;
        }

        internal Dictionary<CompositionLocal, CompositionLocalValueHolder> Values { get; }
        internal CompositionLocalScope Scope { get; }
    }
}
