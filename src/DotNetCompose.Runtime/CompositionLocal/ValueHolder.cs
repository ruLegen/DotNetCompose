using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.CompositionLocal
{
    internal abstract class ValueHolder
    {
        public abstract object? ReadValue(CompositionLocalMap map);
    }

    internal sealed class StaticValueHolder : ValueHolder
    {
        public object? Value { get; }

        public StaticValueHolder(object? value)
        {
            Value = value;
        }

        public override object? ReadValue(CompositionLocalMap map) => Value;
    }

    internal sealed class DynamicValueHolder : ValueHolder
    {
        public object? State { get; set; }

        public DynamicValueHolder(object? state)
        {
            State = state;
        }

        public override object? ReadValue(CompositionLocalMap map)
        {
            if (State is IMutableState mutableState)
                return mutableState.Value;
            return State;
        }
    }

    internal sealed class LazyValueHolder : ValueHolder
    {
        private readonly Func<object?> _factory;
        private object? _value;
        private bool _initialized;

        public LazyValueHolder(Func<object?> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public override object? ReadValue(CompositionLocalMap map)
        {
            if (!_initialized)
            {
                _value = _factory();
                _initialized = true;
            }
            return _value;
        }
    }

    internal sealed class ComputedValueHolder : ValueHolder
    {
        private readonly Func<CompositionLocalMap, object?> _compute;

        public ComputedValueHolder(Func<CompositionLocalMap, object?> compute)
        {
            _compute = compute ?? throw new ArgumentNullException(nameof(compute));
        }

        public override object? ReadValue(CompositionLocalMap map) => _compute(map);
    }

    internal interface IMutableState
    {
        object? Value { get; set; }
    }

    internal class MutableStateWrapper : IMutableState
    {
        private object? _value;

        public MutableStateWrapper(object? value)
        {
            _value = value;
        }

        public object? Value
        {
            get => _value;
            set => _value = value;
        }
    }
}
