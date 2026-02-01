using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public interface IComposeContext
    {
        void StartRoot();
        void EndRoot();
        void StartGroup(int v);
        void EndGroup(int v);
    }
}
