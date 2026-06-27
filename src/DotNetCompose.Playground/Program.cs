using DotNetCompose.Runtime;
using System.Diagnostics;
using static System.Runtime.InteropServices.JavaScript.JSType;
namespace DotNetCompose.Playground
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ComposeContext context1 = new ComposeContext();

            for (int i = 0; i < 2; i++)
            {
                //Composables.CurrentContext?.StartGroup(3);
                using (var r = ComposeScope.CreateScope(context1))
                {
                    //TestClass.Builders.EmptyComposable(0, context1, 3);
                    //TestClass23.Builders.DD2
                    //TestClass.Builders.dd(context1, 0);
                }
                context1.Tree();
            }



        }

        public static void Test(Span<int> changed)
        {
            Span<int> parameters = stackalloc int[4];
            R<int>(parameters, () =>
            {
                Span<int> localParams = stackalloc int[2];
                R<long>(localParams, null);
            });
        }

        public static void R<T>(Span<int> ints, Action action) { }
    }


    class ComposeContext : IComposeContext
    {
        private const int ROOT_KEY = -1000;

        record Group(int ID, Group? Parent, bool Restartable, Dictionary<object, object?> LastValues);
        private List<Group> Groups { get; } = new List<Group>();
        private Stack<int> GroupStackIndecies { get; } = new Stack<int>();
        private bool _skipped;

        public void StartRoot()
        {
            Groups.Clear();
            Start(ROOT_KEY);
            _skipped = false;
        }
        public void EndRoot()
        {
            EndGroup(ROOT_KEY);
        }
        public void StartReplaceableGroup(int groupId)
        {
            Start(groupId, false);
        }

        public void EndReplaceableGroup(int groupId)
        {
            EndGroup(groupId);
        }

        public void StartMoveableGroup(int groupId)
        {
            Start(groupId, false);
        }

        public void EndMoveableGroup(int groupId)
        {
            EndGroup(groupId);
        }
        public void StartRestartableGroup(int groupId)
        {
            Start(groupId, true);
            _skipped = false;
        }

        public void EndGroup(int v)
        {
            GroupStackIndecies.Pop();
        }

        public IComposeUpdateScope? EndRestartableGroup(int groupId)
        {
            EndGroup(groupId);
            return null;
        }

        private void Start(int id, bool restartable = false)
        {
            Group parent = null;
            if (GroupStackIndecies.TryPeek(out int index))
            {
                parent = Groups[index];
            }
            Groups.Add(new Group(id, parent, restartable, new Dictionary<object, object?>()));
            GroupStackIndecies.Push(Groups.Count - 1);
        }

        public bool Changed<T>(T value)
        {
            if (!GroupStackIndecies.TryPeek(out int index))
                return true;
            var group = Groups[index];
            var key = (typeof(T), value);
            if (group.LastValues.TryGetValue(key, out var lastValue))
            {
                return !EqualityComparer<T>.Default.Equals(value, (T)lastValue);
            }
            group.LastValues[key] = value;
            return true;
        }

        public bool Skipping
        {
            get
            {
                if (!GroupStackIndecies.TryPeek(out int index))
                    return false;
                var group = Groups[index];
                return group.Restartable && !_skipped;
            }
        }

        public void SkipToGroupEnd()
        {
            _skipped = true;
        }

        public void Tree()
        {
            var g = Groups.GroupBy(g => g.Parent);
        }


    }
}
