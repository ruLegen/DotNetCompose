using System;
using System.Collections.Generic;
using System.Text;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public interface IComposeUpdateScope
    {
        void UpdateScope(Action<IComposerContext> scopeUpdater);
    }
}
