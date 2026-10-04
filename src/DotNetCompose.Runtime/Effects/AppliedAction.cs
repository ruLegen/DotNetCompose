using System;

namespace DotNetCompose.Runtime.Effects
{
    // A fresh slot value commits configuration without touching a retained object's fields during computation.
    internal sealed class AppliedAction : IRememberObserver
    {
        private readonly Action _apply;
        internal AppliedAction(Action apply) => _apply = apply;
        public void OnRemembered() => _apply();
        public void OnForgotten() { }
        public void OnAbandoned() { }
    }
}
