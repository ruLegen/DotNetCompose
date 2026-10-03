using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class AsyncOwner
    {
        private readonly SynchronizationContext _context;
        private readonly Action<Exception> _errorSink;
        private readonly int _thread = Environment.CurrentManagedThreadId;

        internal AsyncOwner(Action<Exception> errorSink)
        {
            _context = SynchronizationContext.Current
                ?? throw new InvalidOperationException("Async UI operations require an application SynchronizationContext.");
            if (_context.GetType() == typeof(SynchronizationContext))
                throw new InvalidOperationException("Async UI operations require a sequential application SynchronizationContext.");
            _errorSink = errorSink;
        }

        internal void VerifyAccess()
        {
            if (_thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Use the owning UI thread.");
        }

        internal void Dispatch(Action action)
        {
            if (_thread == Environment.CurrentManagedThreadId)
                Run(action);
            else
                _context.Post(_ => Run(action), null);
        }

        private void Run(Action action)
        {
            VerifyAccess();
            SynchronizationContext? previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_context);
            try { action(); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }

        internal void Report(Exception error) => _context.Post(_ => _errorSink(error), null);

        internal static bool IsCancellation(Exception error, CancellationToken token) =>
            error is OperationCanceledException cancelled && token.IsCancellationRequested &&
            (cancelled.CancellationToken == token || cancelled.CancellationToken == default);

        internal static void Observe(Task task) => task.ContinueWith(
            completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        internal void Cancel(CancellationTokenSource? source)
        {
            try { source?.Cancel(); }
            catch (Exception error) { Report(error); }
        }
    }
}
