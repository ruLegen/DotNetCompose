using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.Runtime.CompositionLocal
{
    /// <summary>
    /// A read-only snapshot of CompositionLocal values at a given point
    /// in the composition tree.
    /// </summary>
    public sealed class CompositionLocalMap
    {
        /// <summary>Empty map with no values.</summary>
        public static readonly CompositionLocalMap Empty = new CompositionLocalMap();

        private readonly Dictionary<object, ValueHolder> _values;

        internal CompositionLocalMap()
        {
            _values = new Dictionary<object, ValueHolder>();
        }

        private CompositionLocalMap(Dictionary<object, ValueHolder> values)
        {
            _values = values;
        }

        internal ValueHolder? GetHolder(object key)
        {
            _values.TryGetValue(key, out var holder);
            return holder;
        }

        internal T? GetValueOrDefault<T>(CompositionLocal<T> key)
        {
            if (_values.TryGetValue(key, out var holder))
                return (T?)holder.ReadValue(this);
            return default;
        }

        internal bool ContainsKey(object key) => _values.ContainsKey(key);

        /// <summary>
        /// Creates a new CompositionLocalMap by merging the provided values
        /// with the parent scope.
        /// </summary>
        internal static CompositionLocalMap Create(
            ProvidedValue[] values,
            CompositionLocalMap parentScope,
            CompositionLocalMap? previous = null)
        {
            if (values == null || values.Length == 0)
                return parentScope;

            var merged = new Dictionary<object, ValueHolder>();

            // Copy parent scope values first
            foreach (var kvp in parentScope._values)
                merged[kvp.Key] = kvp.Value;

            // Apply provided values
            for (int i = 0; i < values.Length; i++)
            {
                var pv = values[i];
                if (pv.IsDefaultOnly && parentScope.ContainsKey(pv.Key))
                    continue;

                var prevHolder = previous?.GetHolder(pv.Key);
                var newHolder = CreateValueHolder(pv, prevHolder);
                merged[pv.Key] = newHolder;
            }

            return new CompositionLocalMap(merged);
        }

        internal static CompositionLocalMap Create(ProvidedValue value, CompositionLocalMap parentScope)
        {
            return Create(new[] { value }, parentScope, null);
        }

        private static ValueHolder CreateValueHolder(ProvidedValue pv, ValueHolder? previous)
        {
            if (previous is DynamicValueHolder dvh && pv.IsDynamic)
            {
                dvh.State = pv.UntypedValue;
                return dvh;
            }

            if (pv.IsDynamic)
                return new DynamicValueHolder(new MutableStateWrapper(pv.UntypedValue));

            return new StaticValueHolder(pv.UntypedValue);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not CompositionLocalMap other) return false;
            if (_values.Count != other._values.Count) return false;
            foreach (var kvp in _values)
            {
                if (!other._values.TryGetValue(kvp.Key, out var otherVal))
                    return false;
                if (!Equals(kvp.Value, otherVal))
                    return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var kvp in _values)
                hash = hash * 31 + kvp.Key.GetHashCode() ^ (kvp.Value?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
