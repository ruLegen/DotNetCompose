using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime
{
    public readonly ref struct ComposableArgumentsDefaultState
    {
        public const byte NotProvided = 0;
        public const byte Provided = 1;

        public static ComposableArgumentsDefaultState Empty => default;

        private readonly ReadOnlySpan<byte> _state;

        public ComposableArgumentsDefaultState()
        {
            _state = ReadOnlySpan<byte>.Empty;
        }

        public ComposableArgumentsDefaultState(ReadOnlySpan<byte> state)
        {
            _state = state;
        }

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state.Length > index ? _state[index] : (byte)0;
        }
    }
}
