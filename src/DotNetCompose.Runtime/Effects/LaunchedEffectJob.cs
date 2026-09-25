using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class LaunchedEffectJob : IRememberObserver
    {
        private readonly Func<CancellationToken, ValueTask> _taskFactory;
        private readonly Action<Exception> _errorSink;
        private CancellationTokenSource? _cts;

        public LaunchedEffectJob(Func<CancellationToken, ValueTask> taskFactory, Action<Exception> errorSink)
        {
            _taskFactory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
            _errorSink = errorSink ?? throw new ArgumentNullException(nameof(errorSink));
        }

        public void OnRemembered()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task task;
            try { task = _taskFactory(token).AsTask(); }
            catch (Exception error) { _errorSink(error); return; }
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && !token.IsCancellationRequested && t.Exception != null)
                    _errorSink(t.Exception);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        public void OnForgotten()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void OnAbandoned()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
