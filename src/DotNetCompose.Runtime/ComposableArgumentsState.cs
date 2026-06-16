using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Text;

namespace DotNetCompose.Runtime
{
    public readonly ref struct ComposableArgumentsState
    {
        public static ComposableArgumentsState Empty => default;
        public ComposableArgumentsState()
        {
            _parametersState = Span<int>.Empty;
        }
        public ComposableArgumentsState(Span<int> parameterStates)
        {
            _parametersState = parameterStates;
        }

        public int this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _parametersState.IsEmpty
                ? 0
                : _parametersState[index];
        }

        private readonly Span<int> _parametersState;
    }
}
