using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public interface IComposeContext
    {
        void StartRoot();
        void EndRoot();

        void StartRestartableGroup(int groupId);
        void EndRestartableGroup(int groupId);


        void StartReplaceableGroup(int groupId);
        void EndReplaceableGroup(int groupId);

        void StartMoveableGroup(int groupId);   
        void EndMoveableGroup(int groupId);

        bool Changed<T>(T value);
        bool Skipping { get; }
        void SkipToGroupEnd();
    }
}
