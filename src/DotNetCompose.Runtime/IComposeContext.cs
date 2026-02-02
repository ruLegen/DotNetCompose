using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public interface IComposeContext
    {
        void StartRoot();
        void EndRoot();
        void StartGroup(int groupId);
        void EndGroup(int groupId);
        void StartRestartableGroup(int groupId);
        void EndRestartableGroup(int groupId);
    }
}
