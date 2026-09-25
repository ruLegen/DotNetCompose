using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Tui.Tests;

internal sealed record ReconcileItem(int Id, string Label);

public sealed class TuiReconciliationTests
{
    private static readonly SnapshotMutableState<IReadOnlyList<ReconcileItem>> Items =
        Composables.CreateMutableState<IReadOnlyList<ReconcileItem>>(Array.Empty<ReconcileItem>());

    private sealed class TestContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue) _queue.Enqueue((d, state));
        }

        public void Drain()
        {
            int limit = 10_000;
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_queue)
                {
                    if (_queue.Count == 0) return;
                    work = _queue.Dequeue();
                }
                Assert.True(limit-- > 0, "Recomposer did not become idle.");
                work.Callback(work.State);
            }
        }
    }

    [Fact]
    public void RandomizedKeyedEditsPreserveRetainedNodeIdentityThroughTuiApplier()
    {
        Items.Value = Enumerable.Range(0, 12).Select(id => new ReconcileItem(id, $"item-{id}")).ToArray();
        TuiApplier applier = new();
        using Composition<TuiNode> composition = new(applier);
        composition.SetContent((composer, _, _) => Tui.Builders.List(
            Items.Value,
            item => item.Id,
            0,
            _ => { },
            TuiLayout.Fill,
            (item, itemComposer, __, ___) => Tui.Builders.Text(item.Label, default, null, itemComposer),
            composer));

        Dictionary<int, TuiNode> identities = CurrentNodes(applier);
        Random random = new(8675309);
        int nextId = 12;
        for (int iteration = 0; iteration < 250; iteration++)
        {
            List<ReconcileItem> next = Items.Value.ToList();
            switch (random.Next(3))
            {
                case 0 when next.Count > 0:
                    next.RemoveAt(random.Next(next.Count));
                    break;
                case 1:
                    next.Insert(random.Next(next.Count + 1), new ReconcileItem(nextId, $"item-{nextId}"));
                    nextId++;
                    break;
                default:
                    next = next.OrderBy(_ => random.Next()).ToList();
                    break;
            }

            Items.Value = next;
            Assert.True(composition.Recompose());
            composition.ApplyChanges();
            Dictionary<int, TuiNode> current = CurrentNodes(applier);
            Assert.Equal(next.Select(item => item.Id), current.Keys);
            foreach ((int id, TuiNode node) in current)
                if (identities.TryGetValue(id, out TuiNode? old)) Assert.Same(old, node);
            identities = current;
        }
    }

    private static Dictionary<int, TuiNode> CurrentNodes(TuiApplier applier)
    {
        ListNode list = Assert.IsType<ListNode>(Assert.Single(applier.Root.Children));
        return list.Children.Cast<TextNode>().ToDictionary(
            node => int.Parse(node.Text.AsSpan("item-".Length)), node => (TuiNode)node);
    }
}
