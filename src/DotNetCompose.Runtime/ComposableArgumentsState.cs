using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using System.Text;

namespace DotNetCompose.Runtime
{
    public readonly ref struct ComposableArgumentsState
    {
        public const byte Uncertain = 0;
        public const byte Different = 1;
        public const byte Same = 2;
        public const byte Static = 3;

        public const byte FORCE = 1;

        private readonly byte _force;
        private readonly Span<byte> _parametersState;

        public static ComposableArgumentsState Empty => default;
        public static ComposableArgumentsState Force => new ComposableArgumentsState(FORCE);
        public ComposableArgumentsState()
        {
            _parametersState = Span<byte>.Empty;
            _force = 0;
        }
        public ComposableArgumentsState(Span<byte> parameterStates)
        {
            _parametersState = parameterStates;
            _force = 0;
        }
        private ComposableArgumentsState(byte force)
        {
            _parametersState = Span<byte>.Empty;
            _force = force;
        }

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                byte res = Uncertain;
                if (!_parametersState.IsEmpty)
                    res = _parametersState[index];
                return (byte)((res & (byte)(_force - 1)) | _force);
            }
        }

      
    }
}
