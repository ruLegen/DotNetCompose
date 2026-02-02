using DotNetCompose.Runtime;
using System.Diagnostics;

namespace DotNetCompose.Playground
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ComposeContext context1 = new ComposeContext();
            for (int i = 0; i < 2; i++)
            {
                using (var r = ComposeScope.CreateScope(context1))
                {
                    TestClass.App(i);
                }
                context1.Tree();
            }

        }


    }


    class ComposeContext : IComposeContext
    {
        private const int ROOT_KEY = -1000;

        record Group(int ID, Group? Parent, bool Restartable);
        private List<Group> Groups { get; } = new List<Group>();
        private Stack<int> GroupStackIndecies { get; } = new Stack<int>();

        public void StartRoot()
        {
            Groups.Clear();
            StartGroup(ROOT_KEY);
        }
        public void EndRoot()
        {
            EndGroup(ROOT_KEY);
        }

        public void StartGroup(int v)
        {
            Start(v, false);
        }
        public void StartRestartableGroup(int groupId)
        {
            Start(groupId,true);
        }

        public void EndGroup(int v)
        {
            GroupStackIndecies.Pop();
        }

        public void EndRestartableGroup(int groupId)
        {
            EndGroup(groupId);
        }

        private void Start(int id, bool restartable = false)
        {
            Group parent = null;
            if (GroupStackIndecies.TryPeek(out int index))
            {
                parent = Groups[index];
            }
            Groups.Add(new Group(id, parent,restartable));
            GroupStackIndecies.Push(Groups.Count - 1);
        }

        public void Tree()
        {
            var g = Groups.GroupBy(g => g.Parent);
        }

    }
}
