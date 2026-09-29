using System;

namespace DotNetCompose.Runtime
{
    // Compiler-facing slot payload for a defaults group. The mask is copied because the
    // generated call can pass a stack-allocated ComposableArgumentsDefaultState.
    public sealed class ComposableDefaultsCache
    {
        private readonly DefaultMaskSnapshot _mask;
        private readonly object?[] _values;

        public ComposableDefaultsCache(ComposableArgumentsDefaultState mask, object?[] values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _mask = DefaultMaskSnapshot.Capture(mask, values.Length);
        }

        public bool Matches(ComposableArgumentsDefaultState mask)
        {
            return _mask.Matches(mask, _values.Length);
        }

        public T Get<T>(int index) => (T)_values[index]!;
    }
}
