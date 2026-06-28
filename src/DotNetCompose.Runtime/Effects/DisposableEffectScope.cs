using System;

namespace DotNetCompose.Runtime.Effects
{
    public sealed class DisposableEffectScope
    {
        public DisposableEffectResult OnDispose(Action action)
        {
            return new DisposableEffectResult(action);
        }
    }
}
