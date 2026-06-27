using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime
{
    public readonly ref struct ComposableArgumentsDefaultState
    {
        public const byte NotProvided = 0;
        public const byte ShouldUseDefault = 1;

        public static ComposableArgumentsDefaultState Empty => default;


        public ComposableArgumentsDefaultState()
        {
            _state = ReadOnlySpan<byte>.Empty;
        }

        public ComposableArgumentsDefaultState(ReadOnlySpan<byte> state)
        {
            _state = state;
        }

        private readonly ReadOnlySpan<byte> _state;

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state.Length > index ? _state[index] : (byte)0;
        }

        public byte[] CloneValues() => _state.ToArray();
    }
}
