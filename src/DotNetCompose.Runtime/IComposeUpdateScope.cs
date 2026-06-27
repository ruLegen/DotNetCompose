using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public interface IComposeUpdateScope
    {
        void UpdateScope(Action<IComposeContext> scopeUpdater);
    }
}
