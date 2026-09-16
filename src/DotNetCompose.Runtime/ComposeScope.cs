using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    // ComposeScope.cs
    public static class ComposeScope
    {
        private static readonly AsyncLocal<IComposerContext?> _currentContext = new AsyncLocal<IComposerContext?>();

        public static IComposerContext? GetCurrentContext() => _currentContext.Value;

        internal static IDisposable EnterContext(IComposerContext context)
        {
            IComposerContext? previous = _currentContext.Value;
            _currentContext.Value = context;
            return new DisposableScope(() => _currentContext.Value = previous);
        }

        //public static IComposerContext GetCurrentOrCreate()
        //{
        //    if (_currentContext.Value == null)
        //    {
        //        _currentContext.Value = new ComposeContext();
        //    }
        //    return _currentContext.Value;
        //}

        public static IDisposable CreateScope(IComposerContext newContext)
        {
            IComposerContext? previous = _currentContext.Value;
            _currentContext.Value = newContext;
            try { newContext.StartRoot(); }
            catch { _currentContext.Value = previous; throw; }
            return new DisposableScope(() =>
            {
                try { newContext.EndRoot(); }
                finally { _currentContext.Value = previous; }
            });
        }

        private class DisposableScope : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;

            public DisposableScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose();
            }
        }
    }
}
