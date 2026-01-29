using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DotNetCompose.Runtime
{
    // ComposeScope.cs
    public static class ComposeScope
    {
        private static readonly AsyncLocal<IComposeContext?> _currentContext = new AsyncLocal<IComposeContext?>();

        public static IComposeContext? GetCurrentContext() => _currentContext.Value;

        //public static IComposeContext GetCurrentOrCreate()
        //{
        //    if (_currentContext.Value == null)
        //    {
        //        _currentContext.Value = new ComposeContext();
        //    }
        //    return _currentContext.Value;
        //}

        public static IDisposable CreateScope(IComposeContext newContext)
        {
            var previous = _currentContext.Value;
            _currentContext.Value = newContext;

            return new DisposableScope(() =>
            {
                _currentContext.Value = previous;
            });
        }

        private class DisposableScope : IDisposable
        {
            private readonly Action _onDispose;

            public DisposableScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose();
            }
        }
    }
}
