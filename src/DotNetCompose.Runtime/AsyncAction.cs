using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Effects;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime
{
    /// <summary>A composition-owned adapter from an asynchronous operation to an Action callback.</summary>
    public sealed class AsyncAction : IRememberObserver
    {
        private readonly AsyncOwner _owner;
        private readonly SnapshotMutableState<bool> _running = Composables.CreateMutableState(false);
        private readonly SnapshotMutableState<Exception?> _error = Composables.CreateMutableState<Exception?>(null);
        private ITaskActivity? _activity;
        private Func<CancellationToken, Task>? _handler;
        private CancellationTokenSource? _source;
        private Task? _execution;
        private bool _mounted;

        internal AsyncAction(AsyncOwner owner) => _owner = owner;
        public bool IsRunning => _running.Value;
        public Exception? Error => _error.Value;

        internal void Configure(ITaskActivity activity, Func<CancellationToken, Task> handler)
        {
            _owner.VerifyAccess();
            if (!ReferenceEquals(_activity, activity))
            {
                if (_activity != null)
                    _activity.Changed -= OnActivityChanged;
                _activity = activity;
                if (_mounted)
                    _activity.Changed += OnActivityChanged;
            }
            _handler = handler;
            if (!activity.IsActive)
                Cancel();
        }

        private void OnActivityChanged(object? sender, EventArgs args)
        {
            ITaskActivity? activity = _activity;
            bool active = activity?.IsActive == true;
            _owner.Dispatch(() =>
            {
                if (_mounted && ReferenceEquals(activity, _activity) && !active)
                    Cancel();
            });
        }

        public void Execute() => AsyncOwner.Observe(ExecuteAsync());

        public Task ExecuteAsync()
        {
            _owner.VerifyAccess();
            if (_execution != null)
                return _execution;
            if (!_mounted || _activity?.IsActive != true || _handler == null)
                return Task.FromCanceled(new CancellationToken(true));

            CancellationTokenSource source = new CancellationTokenSource();
            TaskCompletionSource<object?> completion = new TaskCompletionSource<object?>();
            _source = source;
            _execution = completion.Task;
            _running.Value = true;
            _error.Value = null;
            Func<CancellationToken, Task> handler = _handler;
            _owner.Dispatch(() => AsyncOwner.Observe(Run(handler, source, completion)));
            return completion.Task;
        }

        private async Task Run(Func<CancellationToken, Task> handler, CancellationTokenSource source, TaskCompletionSource<object?> completion)
        {
            CancellationToken token = source.Token;
            Exception? failure = null;
            bool cancelled = false;
            try
            {
                await handler(token);
                cancelled = token.IsCancellationRequested;
            }
            catch (Exception error)
            {
                if (AsyncOwner.IsCancellation(error, token))
                    cancelled = true;
                else
                {
                    failure = error;
                    if (_mounted)
                        _error.Value = error;
                    else
                        _owner.Report(error);
                }
            }
            finally
            {
                _source = null;
                _execution = null;
                if (_mounted)
                    _running.Value = false;
                source.Dispose();
            }
            if (failure != null)
                completion.TrySetException(failure);
            else if (cancelled)
                completion.TrySetCanceled(token);
            else
                completion.TrySetResult(null);
        }

        public void Cancel()
        {
            _owner.VerifyAccess();
            _owner.Cancel(_source);
        }

        void IRememberObserver.OnRemembered() => _mounted = true;
        void IRememberObserver.OnForgotten()
        {
            _mounted = false;
            if (_activity != null)
                _activity.Changed -= OnActivityChanged;
            Cancel();
        }
        void IRememberObserver.OnAbandoned() => ((IRememberObserver)this).OnForgotten();
    }
}
