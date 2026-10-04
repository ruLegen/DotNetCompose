using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCompose.Runtime.Effects
{
    internal sealed class ActivityEffect<TKey> : IRememberObserver
    {
        private readonly AsyncOwner _owner;
        private readonly bool _serial;
        private readonly Action? _invalidated;
        private ITaskActivity? _activity;
        private Func<CancellationToken, long, ValueTask>? _block;
        private CancellationTokenSource? _source;
        private TKey _key = default!;
        private long _generation;
        private bool _configured;
        private bool _mounted;
        private bool _active;
        private bool _requested;
        private bool _running;

        internal ActivityEffect(AsyncOwner owner, bool serial = true, Action? invalidated = null)
        {
            _owner = owner;
            _serial = serial;
            _invalidated = invalidated;
        }

        internal bool IsCurrent(long generation) => _mounted && _active && _generation == generation;

        internal void Configure(TKey key, ITaskActivity activity, Func<CancellationToken, long, ValueTask> block)
        {
            _owner.VerifyAccess();
            bool changed = !_configured || !EqualityComparer<TKey>.Default.Equals(_key, key) || !ReferenceEquals(_activity, activity);
            if (!ReferenceEquals(_activity, activity))
            {
                if (_activity != null)
                    _activity.Changed -= OnActivityChanged;
                _activity = activity;
                if (_mounted)
                    _activity.Changed += OnActivityChanged;
            }
            _configured = true;
            _key = key;
            _block = block;
            bool active = activity.IsActive;
            if (changed || active != _active)
            {
                _active = active;
                Invalidate();
            }
        }

        private void OnActivityChanged(object? sender, EventArgs args) => _owner.Dispatch(() =>
        {
            if (!_mounted || _activity == null || _active == _activity.IsActive)
                return;
            _active = _activity.IsActive;
            Invalidate();
        });

        private void Invalidate()
        {
            _generation++;
            _requested = _mounted && _active;
            _owner.Cancel(_source);
            _invalidated?.Invoke();
            if (!_serial)
            {
                _source = null;
                _running = false;
            }
            StartRequested();
        }

        private void StartRequested()
        {
            if (!_requested || _running || _block == null)
                return;
            _requested = false;
            _running = true;
            CancellationTokenSource source = new CancellationTokenSource();
            _source = source;
            long generation = _generation;
            Func<CancellationToken, long, ValueTask> block = _block;
            _owner.Dispatch(() => AsyncOwner.Observe(Run(source, generation, block)));
        }

        private async Task Run(CancellationTokenSource source, long generation, Func<CancellationToken, long, ValueTask> block)
        {
            CancellationToken token = source.Token;
            try { await block(token, generation); }
            catch (Exception error)
            {
                if (!AsyncOwner.IsCancellation(error, token))
                    _owner.Report(error);
            }
            finally
            {
                source.Dispose();
                if (ReferenceEquals(_source, source))
                {
                    _source = null;
                    _running = false;
                    StartRequested();
                }
            }
        }

        public void OnRemembered() => _mounted = true;

        public void OnForgotten()
        {
            _mounted = false;
            _generation++;
            _requested = false;
            if (_activity != null)
                _activity.Changed -= OnActivityChanged;
            _owner.Cancel(_source);
        }

        public void OnAbandoned() => OnForgotten();
    }
}
