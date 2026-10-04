using System;
using System.Linq;

namespace DotNetCompose.Runtime
{
    public static class TaskActivity
    {
        public static ITaskActivity Always { get; } = new AlwaysActivity();

        /// <summary>Combines conditions; subscriptions exist only while the result has listeners.</summary>
        public static ITaskActivity All(params ITaskActivity[] activities)
        {
            if (activities == null)
                throw new ArgumentNullException(nameof(activities));
            if (activities.Any(activity => activity == null))
                throw new ArgumentException("Activities cannot contain null.", nameof(activities));
            return activities.Length == 0 ? Always : new AllActivity((ITaskActivity[])activities.Clone());
        }

        private sealed class AlwaysActivity : ITaskActivity
        {
            public bool IsActive => true;
            public event EventHandler? Changed { add { } remove { } }
        }

        private sealed class AllActivity : ITaskActivity
        {
            private readonly ITaskActivity[] _activities;
            private EventHandler? _changed;
            private bool _lastActive;

            internal AllActivity(ITaskActivity[] activities) => _activities = activities;
            public bool IsActive => _activities.All(activity => activity.IsActive);

            public event EventHandler? Changed
            {
                add
                {
                    if (value == null)
                        return;
                    if (_changed == null)
                    {
                        _lastActive = IsActive;
                        foreach (ITaskActivity activity in _activities)
                            activity.Changed += OnChanged;
                    }
                    _changed += value;
                }
                remove
                {
                    if (value == null || _changed == null)
                        return;
                    _changed -= value;
                    if (_changed == null)
                        foreach (ITaskActivity activity in _activities)
                            activity.Changed -= OnChanged;
                }
            }

            private void OnChanged(object? sender, EventArgs args)
            {
                bool active = IsActive;
                if (active == _lastActive)
                    return;
                _lastActive = active;
                _changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
