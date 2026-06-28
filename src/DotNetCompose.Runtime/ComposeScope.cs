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
            var previous = _currentContext.Value;
            _currentContext.Value = newContext;
            newContext.StartRoot();
            return new DisposableScope(() =>
            {
                newContext.EndRoot();
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
