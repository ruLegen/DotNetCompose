using System;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class DisposableEffectJob : IRememberObserver
    {
        private readonly Func<IDisposable> _setup;
        private readonly Action<Exception> _errorSink;
        private IDisposable? _resource;

        internal DisposableEffectJob(Func<IDisposable> setup, Action<Exception> errorSink)
        {
            _setup = setup;
            _errorSink = errorSink;
        }

        public void OnRemembered()
        {
            try
            {
                _resource = _setup() ?? throw new InvalidOperationException("DisposableEffect setup returned null.");
            }
            catch (Exception error)
            {
                _errorSink(error);
            }
        }

        public void OnForgotten()
        {
            IDisposable? resource = _resource;
            _resource = null;
            resource?.Dispose();
        }

        public void OnAbandoned()
        {
            OnForgotten();
        }
    }


    internal sealed class DisposableEffectAction : IRememberObserver
    {
        private Action? _action;
        private readonly Action<Exception> _errorSink;

        internal DisposableEffectAction(Action? action, Action<Exception> errorSink)
        {
            _action = action;
            _errorSink = errorSink;
        }

        public void OnRemembered()
        {
        }

        public void OnForgotten()
        {
            Action? action = _action;
            _action = null;
            action?.Invoke();
        }

        public void OnAbandoned()
        {
            OnForgotten();
        }
    }
}
