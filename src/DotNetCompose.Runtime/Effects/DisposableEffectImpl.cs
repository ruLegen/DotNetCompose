using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class DisposableEffectImpl : IRememberObserver
    {
        private readonly Func<DisposableEffectScope, DisposableEffectResult> _effect;
        private DisposableEffectResult? _result;

        public DisposableEffectImpl(Func<DisposableEffectScope, DisposableEffectResult> effect)
        {
            _effect = effect ?? throw new ArgumentNullException(nameof(effect));
        }

        public void OnRemembered()
        {
            _result?.Dispose();
            _result = _effect(new DisposableEffectScope());
        }

        public void OnForgotten()
        {
            _result?.Dispose();
            _result = null;
        }

        public void OnAbandoned()
        {
        }
    }
}
