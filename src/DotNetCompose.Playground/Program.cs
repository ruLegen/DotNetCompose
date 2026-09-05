using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;
using System.Diagnostics;
namespace DotNetCompose.Playground
{
    internal class Program
    {
        static void Main(string[] args)
        {

            for (int i = 0; i < 2; i++)
            {
                           }

            Console.WriteLine("Hello, World!");
            var state = new SnapshotMutableState<int>(0, StructuralPolicy<int>.Default);
            var state2 = new SnapshotMutableState<int>(0, StructuralPolicy<int>.Default);
            var s = Snapshot.TakeMutableSnapshot();

            state.Value = 1;
            PrintState("Befor snap", state);
            PrintState("Befor snap", state2);
            s.Enter(() =>
            {
                state2.Value = 333;
                state.Value = 2;
                PrintState("In snap 1", state);
                PrintState("In snap 1_2", state2);
                var rr = Snapshot.TakeMutableSnapshot();
                rr.Enter(() =>
                {
                    state.Value++;
                    PrintState("In snap 2", state);
                });
            });
            PrintState("after snap", state);
            Console.ReadLine();
        }

        static void PrintState<T>(string msg, SnapshotMutableState<T> state) => Console.WriteLine(msg + " " + state.ToString());

        int t()
        {
            IDisposable f = null;
            using (f)
            {
                return 9;
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


    class ComposeContext : IComposerContext
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

        public void StartMovableGroup(int groupId)
        {
            Start(groupId, false);
        }

        public void EndMovableGroup(int groupId)
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

        public void StartGroup(int key) => Start(key);
        public void EndGroup() => GroupStackIndecies.Pop();

        public object? RememberedValue()
        {
            if (!GroupStackIndecies.TryPeek(out int index))
                return null;
            var group = Groups[index];
            return group.LastValues.TryGetValue("__remembered", out var v) ? v : null;
        }

        public void UpdateRememberedValue(object? value)
        {
            if (GroupStackIndecies.TryPeek(out int index))
                Groups[index].LastValues["__remembered"] = value;
        }

        public void CreateNode<T>(Func<T> factory) where T : class { }
        public void ApplyNode<T>(Action<T> block, object? value) { }

        public void ComposeContent(ComposableAction content)
        {
            StartRoot();
            try { content(this, default, default); }
            finally { EndRoot(); }
        }

        public bool Inserting => false;
        public bool IsComposing { get; private set; }

        public void Dispose() { }

        public void Tree()
        {
            var g = Groups.GroupBy(g => g.Parent);
        }


    }
}
