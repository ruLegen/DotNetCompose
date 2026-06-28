using System;

namespace DotNetCompose.Runtime.Effects
{
    public sealed class DisposableEffectResult : IDisposable
    {
        private Action? _dispose;
        private bool _disposed;

        public DisposableEffectResult(Action dispose)
        {
            _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var action = _dispose;
            _dispose = null;
            action!();
        }
    }
}
