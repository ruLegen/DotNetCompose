using System;
using System.Runtime.CompilerServices;

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

        // A restart scope is forced because one of the states read by its body was invalidated,
        // while its captured arguments can still be unchanged. Force therefore controls whether
        // this group executes and does not supply dirty states for its parameters.
        public static ComposableArgumentsState Force => new ComposableArgumentsState(FORCE);

        // Generated skip logic checks force separately from parameter states. Combining them
        // would make an invalidated parent report Different for every argument and needlessly
        // execute unchanged children.
        public bool IsForced => _force == FORCE;

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

        // A generated restart must execute its own group, but child calls still need the original
        // argument states to make correct skip decisions and preserve comparison-slot ownership.
        // The span only has to live for the synchronous composable invocation.
        public static ComposableArgumentsState Forced(Span<byte> parameterStates) =>
            new ComposableArgumentsState(parameterStates, FORCE);

        private ComposableArgumentsState(byte force)
        {
            _parametersState = Span<byte>.Empty;
            _force = force;
        }

        private ComposableArgumentsState(Span<byte> parameterStates, byte force)
        {
            _parametersState = parameterStates;
            _force = force;
        }

        // Different described the transition handled by the original invocation. A restart
        // lambda passes the value captured by that invocation again, so it is Same relative to
        // the committed composition. Uncertain must stay Uncertain because the callee owns its
        // comparison slot and must consume it again; Same and Static already need no comparison.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte NormalizeForRestart(byte state) => state == Different ? Same : state;

        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_parametersState.IsEmpty)
                    return Uncertain;
                return _parametersState[index];
            }
        }
    }
}
