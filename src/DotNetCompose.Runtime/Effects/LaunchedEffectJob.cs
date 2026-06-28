using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class LaunchedEffectJob : IRememberObserver
    {
        private readonly Func<CancellationToken, ValueTask> _taskFactory;
        private CancellationTokenSource? _cts;

        public LaunchedEffectJob(Func<CancellationToken, ValueTask> taskFactory)
        {
            _taskFactory = taskFactory ?? throw new ArgumentNullException(nameof(taskFactory));
        }

        public void OnRemembered()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var task = _taskFactory(token).AsTask();
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var _ = t.Exception;
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
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
