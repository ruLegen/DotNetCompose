using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class AsyncData<TKey, T> : IRememberObserver
    {
        private readonly ActivityEffect<TKey> _effect;
        private readonly AsyncOwner _owner;
        private TKey _key = default!;
        private bool _configured;

        internal AsyncData(AsyncOwner owner, bool serial)
        {
            _owner = owner;
            State = Composables.CreateMutableState(default(AsyncState<T>));
            _effect = new ActivityEffect<TKey>(owner, serial, () => State.Value = State.Value.Pause());
        }

        internal SnapshotMutableState<AsyncState<T>> State { get; }

        internal void Prepare(TKey key, ITaskActivity activity)
        {
            if (!_configured || !EqualityComparer<TKey>.Default.Equals(_key, key))
                State.Value = AsyncState<T>.Initial(activity.IsActive);
        }

        internal void Configure(TKey key, ITaskActivity activity, Func<CancellationToken, Task<T>> factory)
        {
            _key = key;
            _configured = true;
            _effect.Configure(key, activity, async (token, generation) =>
            {
                State.Value = State.Value.Begin();
                try
                {
                    T result = await factory(token);
                    if (_effect.IsCurrent(generation))
                        State.Value = State.Value.WithValue(result, true);
                }
                catch (Exception error) { CompleteError(error, token, generation); }
            });
        }

        internal void Configure(TKey key, ITaskActivity activity, Func<CancellationToken, IAsyncEnumerable<T>> factory)
        {
            _key = key;
            _configured = true;
            _effect.Configure(key, activity, async (token, generation) =>
            {
                State.Value = State.Value.Begin();
                try
                {
                    await foreach (T value in factory(token).WithCancellation(token))
                    {
                        if (!_effect.IsCurrent(generation))
                            break;
                        State.Value = State.Value.WithValue(value, false);
                    }
                    if (_effect.IsCurrent(generation))
                        State.Value = State.Value.Complete();
                }
                catch (Exception error) { CompleteError(error, token, generation); }
            });
        }

        private void CompleteError(Exception error, CancellationToken token, long generation)
        {
            if (AsyncOwner.IsCancellation(error, token))
            {
                if (_effect.IsCurrent(generation))
                    State.Value = State.Value.Pause();
            }
            else if (_effect.IsCurrent(generation))
                State.Value = State.Value.Complete(error);
            else
                _owner.Report(error);
        }

        public void OnRemembered() => _effect.OnRemembered();
        public void OnForgotten() => _effect.OnForgotten();
        public void OnAbandoned() => _effect.OnAbandoned();
    }
}
