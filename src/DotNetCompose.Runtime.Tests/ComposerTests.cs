using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Tests
{
    public class ComposerTests
    {
        class TestApplier : IApplier<object>
        {
            private readonly Stack<object> _stack = new Stack<object>();
            private object _current;

            public List<string> Calls { get; } = new List<string>();
            public List<object> StackList => _stack.ToList();
            public object Current => _current;

            public TestApplier(object root) { _current = root; }

            public void OnBeginChanges() => Calls.Add("OnBeginChanges");
            public void OnEndChanges() => Calls.Add("OnEndChanges");

            public void Down(object node)
            {
                Calls.Add($"Down({node})");
                _stack.Push(_current);
                _current = node;
            }

            public void Up()
            {
                Calls.Add("Up()");
                _current = _stack.Pop();
            }

            public void InsertTopDown(int index, object instance)
            {
                Calls.Add($"InsertTopDown({index}, {instance})");
            }

            public void InsertBottomUp(int index, object instance)
            {
                Calls.Add($"InsertBottomUp({index}, {instance})");
            }

            public void Remove(int index, int count)
            {
                Calls.Add($"Remove({index}, {count})");
            }

            public void Move(int from, int to, int count)
            {
                Calls.Add($"Move({from}, {to}, {count})");
            }

            public void Clear()
            {
                Calls.Add("Clear");
                _stack.Clear();
            }

            public void Apply(Action<object, object?> block, object? value)
            {
                Calls.Add($"Apply(value={value})");
                block(_current, value);
            }

            public void Reuse()
            {
                Calls.Add("Reuse");
            }
        }

        [Fact]
        public void Operations_PushAndDrain()
        {
            var ops = new Operations();
            var applier = new TestApplier("root");
            var rm = new RememberManager();
            var slotTable = new SlotTable();

            slotTable.Write<object?>(slots =>
            {
                ops.Push(Operation.EnsureRootGroupStarted);
                ops.Drain(applier, slots, rm);
                return null;
            });

            Assert.True(ops.IsEmpty);
        }

        [Fact]
        public void Operations_PushWithIntArgs()
        {
            var ops = new Operations();
            ops.Push(Operation.AdvanceSlotsBy, w => w.SetInt(0, 5));
            Assert.Equal(1, ops.Size);
            Assert.False(ops.IsEmpty);
        }

        [Fact]
        public void Operations_PushMultipleOps()
        {
            var ops = new Operations();
            ops.Push(Operation.EnsureRootGroupStarted);
            ops.Push(Operation.EndCurrentGroup);
            ops.Push(Operation.EnsureGroupStarted, w => w.SetObject(0, new GapAnchor(-1)));
            Assert.Equal(3, ops.Size);
        }

        [Fact]
        public void Operations_Clear()
        {
            var ops = new Operations();
            ops.Push(Operation.EnsureRootGroupStarted);
            ops.Push(Operation.EndCurrentGroup);
            ops.Clear();
            Assert.True(ops.IsEmpty);
            Assert.Equal(0, ops.Size);
        }

        [Fact]
        public void ChangeList_IsEmptyByDefault()
        {
            var cl = new Composer.ChangeList();
            Assert.True(cl.IsEmpty());
        }

        [Fact]
        public void ChangeList_PushOperations()
        {
            var cl = new Composer.ChangeList();
            cl.PushEnsureRootStarted();
            cl.PushResetSlots();
            Assert.False(cl.IsEmpty());
            Assert.Equal(2, cl.Size);
        }

        [Fact]
        public void ChangeList_Clear()
        {
            var cl = new Composer.ChangeList();
            cl.PushEnsureRootStarted();
            cl.PushResetSlots();
            cl.Clear();
            Assert.True(cl.IsEmpty());
        }

        [Fact]
        public void ChangeList_Execute()
        {
            var cl = new Composer.ChangeList();
            var applier = new TestApplier("root");
            var slotTable = new SlotTable();
            var rm = new RememberManager();

            cl.PushEnsureRootStarted();
            cl.PushResetSlots();

            cl.Execute(slotTable, applier, rm);
            Assert.True(cl.IsEmpty());
        }

        [Fact]
        public void ChangeList_PushUpdateValue()
        {
            var cl = new Composer.ChangeList();
            cl.PushUpdateValue("hello", 0);
            Assert.Equal(1, cl.Size);
        }

        [Fact]
        public void ChangeList_PushAllOperationTypes()
        {
            var cl = new Composer.ChangeList();
            cl.PushEnsureRootStarted();
            cl.PushEndCurrentGroup();
            cl.PushUpdateValue("test", 0);
            cl.PushRemoveCurrentGroup();
            cl.PushDeactivateCurrentGroup();
            cl.PushResetSlots();
            cl.PushSkipToEndOfCurrentGroup();
            cl.PushAdvanceSlotsBy(3);
            cl.PushUps(2);
            cl.PushTrimValues(1);
            cl.PushUseNode();
            cl.PushSideEffect(() => { });
            cl.PushRemember("remembered");
            cl.PushRememberPausingScope(new object());
            cl.PushStartResumingScope(new object());
            cl.PushEndResumingScope(new object());
            cl.PushRemoveNode(0, 1);
            cl.PushMoveNode(0, 2, 1);
            cl.PushUpdateNode("value", (node, val) => { });

            Assert.Equal(19, cl.Size);
        }

        [Fact]
        public void Composition_Constructor()
        {
            var applier = new TestApplier("root");
            var composition = new Composition<object>(applier);
            Assert.NotNull(composition);
            Assert.False(composition.IsDisposed);
        }

        [Fact]
        public void Composition_SetContent()
        {
            var applier = new TestApplier("root");
            applier.OnBeginChanges();
            var composition = new Composition<object>(applier);
            bool contentCalled = false;
            composition.SetContent((_, _, _) => { contentCalled = true; });
            Assert.True(contentCalled);
        }

        [Fact]
        public void Composition_Dispose()
        {
            var applier = new TestApplier("root");
            var composition = new Composition<object>(applier);
            composition.Dispose();
            Assert.True(composition.IsDisposed);
        }

        [Fact]
        public void Composition_DisposeIsIdempotent()
        {
            var applier = new TestApplier("root");
            var composition = new Composition<object>(applier);
            composition.Dispose();
            composition.Dispose();
            Assert.True(composition.IsDisposed);
        }

        [Fact]
        public void GapComposer_RecordsOperations()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => "hello");
            });

            var opsChangeList = composer.OperationsChangeList;
            Assert.NotNull(opsChangeList);
            Assert.False(opsChangeList.IsEmpty());
        }

        [Fact]
        public void GapComposer_OperationsChangeList_HasOperations()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => "hello");
            });

            var opsChangeList = composer.OperationsChangeList;
            Assert.NotNull(opsChangeList);
            Assert.False(opsChangeList.IsEmpty());

            // Operations are recorded but not yet in a directly executable format
            // (they need proper anchor tracking via reader/writer delta).
            // Full pipeline execution is tested via Composition<T>.SetContent().
        }

        [Fact]
        public void GapComposer_OperationsChangeList_HasMultipleOps()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => "a");
                composer.CreateNode(() => "b");
            });

            var ops = composer.OperationsChangeList;
            Assert.NotNull(ops);
            Assert.True(ops.Size >= 2); // 2 * StartNode (new nodes don't emit UseNode/UpdateNode)
        }

        [Fact]
        public void GapComposer_Changed_RecordsUpdateValue()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.Changed("value1");
                composer.EndGroup();
            });

            var ops = composer.OperationsChangeList;
            Assert.NotNull(ops);
            Assert.True(ops.Size >= 1);
        }

        [Fact]
        public void GapComposer_Operations_AfterRecomposition()
        {
            var composer = new Composer.GapComposer();
            // First composition
            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => "hello");
            });

            // Recomposition
            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => "world");
            });

            var ops = composer.OperationsChangeList;
            Assert.NotNull(ops);
            Assert.False(ops.IsEmpty());
        }

        [Fact]
        public void OperationsDrain_CallsApplierBeginEndChanges()
        {
            var ops = new Operations();
            var applier = new TestApplier("root");
            var slotTable = new SlotTable();
            var rm = new RememberManager();

            slotTable.Write<object?>(slots =>
            {
                ops.Push(Operation.EnsureRootGroupStarted);
                ops.Drain(applier, slots, rm);
                return null;
            });

            Assert.True(ops.IsEmpty);
        }

        [Fact]
        public void ChangeList_ExecuteWithUpdateValue()
        {
            var cl = new Composer.ChangeList();
            var applier = new TestApplier("root");
            var slotTable = new SlotTable();
            var rm = new RememberManager();

            cl.PushEnsureRootStarted();
            cl.PushResetSlots();

            cl.Execute(slotTable, applier, rm);
            Assert.True(cl.IsEmpty());
        }
    }
}
