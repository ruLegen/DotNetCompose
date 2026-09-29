using System;

namespace DotNetCompose.Runtime.Snapshots
{
    public class ObserverHandle : IDisposable
    {
        private Action? _unregister;
        private bool _disposed;

        internal ObserverHandle(Action unregister)
        {
            _unregister = unregister;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _unregister?.Invoke();
                _unregister = null;
            }
        }
    }
}
