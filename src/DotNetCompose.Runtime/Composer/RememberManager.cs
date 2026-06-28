using System;
using System.Collections.Generic;
using DotNetCompose.Runtime.Effects;

namespace DotNetCompose.Runtime.Composer
{
    internal class RememberManager
    {
        private readonly List<Action> _sideEffects = new List<Action>();
        private readonly List<IRememberObserver> _pendingRemember = new List<IRememberObserver>();
        private readonly List<IRememberObserver> _pendingForget = new List<IRememberObserver>();
        private readonly HashSet<IRememberObserver> _activeObservers = new HashSet<IRememberObserver>();

        public void SideEffect(Action effect)
        {
            _sideEffects.Add(effect);
        }

        public void Remember(IRememberObserver observer)
        {
            if (observer != null)
                _pendingRemember.Add(observer);
        }

        public void Forget(IRememberObserver observer)
        {
            if (observer != null && _activeObservers.Remove(observer))
                _pendingForget.Add(observer);
        }

        /// <summary>
        /// Dispatches pending lifecycle events.
        /// Forgets are dispatched first (old is cleaned up before new is set up),
        /// then remembers are dispatched.
        /// </summary>
        public void DispatchLifecycle()
        {
            foreach (var observer in _pendingForget)
                observer.OnForgotten();
            _pendingForget.Clear();

            foreach (var observer in _pendingRemember)
            {
                _activeObservers.Add(observer);
                observer.OnRemembered();
            }
            _pendingRemember.Clear();
        }

        public void DispatchSideEffects()
        {
            foreach (var effect in _sideEffects)
                effect();
            _sideEffects.Clear();
        }

        public void Clear()
        {
            foreach (var observer in _activeObservers)
                observer.OnForgotten();
            _activeObservers.Clear();
            _pendingRemember.Clear();
            _pendingForget.Clear();
            _sideEffects.Clear();
        }
    }
}
