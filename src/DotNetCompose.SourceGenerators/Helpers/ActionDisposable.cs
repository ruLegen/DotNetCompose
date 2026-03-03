using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators.Helpers
{
    internal class ActionDisposable : IDisposable
    {
        public ActionDisposable(Action action)
        {
            _action = action;
        }

        private Action _action;

        public void Dispose()
        {
            _action.Invoke();
        }
    }
}
