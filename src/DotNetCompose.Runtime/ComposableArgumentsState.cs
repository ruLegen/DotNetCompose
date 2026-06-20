using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Text;

namespace DotNetCompose.Runtime
{
    public readonly ref struct ComposableArgumentsState
    {
        public const int Same = 0;
        public const int Different = 1;
        public const int Uncertain = 2;

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
                ? Same
                : _parametersState[index];
        }

        private readonly Span<int> _parametersState;
    }
}
