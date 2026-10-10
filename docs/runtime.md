# Composition runtime

`DotNetCompose.Runtime` is the engine that keeps a composition alive between
executions. It retains remembered values, observes state, supports recomposition,
and turns changes in the description into operations on a target node tree.

Start with [Core concepts](concepts.md), then the [basic reading path in the
Source Generator guide](source-generator.md#basic-reading-path). Those sections
explain authoring calls, the injected composition context, and generated entry
points. You can leave advanced modes and computed defaults until after this page.
The runtime uses the generated group operations, argument tracking, and restart
delegates to maintain the description between executions.

A **composition pass** evaluates content and computes its changes. The
**composition context** tracks its current memory position and records operations;
the host's **application context** is the dispatcher that executes host work
sequentially on its owning thread. They have different responsibilities.

## The Compose runtime model

The runtime follows the same broad model as the Kotlin Compose runtime. If you
know Compose, groups, remembered slots, state observation, skipping, and nodes
will be familiar. DotNetCompose uses these ideas to support the same programming
model in C#, with its source generator supplying the composition protocol.

| Mechanism | What it does here |
| --- | --- |
| **Groups** | Identify regions of composable execution and relate them to previous passes. |
| **Slots** | Retain values at positions in a group, including remembered objects and earlier arguments. |
| **State** | Records reads so a change can mark dependent content for another execution. |
| **Skipping** | Reuses retained content when its body does not need to run again. |
| **Nodes** | Connect part of the composition to objects in the target tree. |

### Groups and remembered values

The generated code enters and leaves groups around the regions it describes.
A restartable group retains a delegate for executing its content again. Its
**restartable scope** tracks state dependencies and that restart callback,
allowing the runtime to revisit it without rerunning all its callers. Other
groups give branches, keyed content, and node-producing code their own structure.

Each group can hold ordered slots. When `RememberState(0)` is visited again at
the same composition position, it retrieves the state object from the retained
slots. Two calls to the same composable method occupy different composition
positions, so each call can retain its own state.
See [remembering values and state](#remembering-values-and-state) for the APIs
that use these slots.

The composer matches current groups to previous groups using their key, kind,
and any explicit data key. `Composables.Key(itemId, content)` supplies an identity
for movable content, so the item's remembered values can follow it when a list
is reordered. The slot table stores the retained groups and their values.
The [keyed content example](#keyed-content) shows how that identity differs from
a key passed to `Remember`.

### State, invalidation, and skipping

**Snapshot state** is observable state whose reads and writes are tracked by
the snapshot system. A read during composition is associated with the active
restartable scope, or the root. A changed value can then **invalidate** those
scopes: mark them as needing execution. The `Recomposer` schedules work on the
host's application context; the runtime revisits affected content using its
restart delegates. [Remembering values and state](#remembering-values-and-state)
shows how to create and retain these values.

**Skipping** means preserving a scope's retained content without executing
its body again when no relevant inputs or dependencies require it.

The generator and runtime cooperate on skipping. Generated code carries argument
change information and, where eligible, compares uncertain arguments through
`Changed(value)`. The runtime also checks whether the scope's observed state or
composition-local environment requires execution. That environment contains
the values supplied to descendants by local providers; the host's application
context determines where work executes. Unchanged arguments alone do not make an
invalidated scope safe to skip.
The [composition locals](#composition-locals) section explains how provider
values reach their readers.

Skipping a parent preserves its remembered slots and retained children. An
invalidated child can still restart without executing the parent's body.
The [mode examples](source-generator.md#composable-modes) explain when the current
generator emits argument comparisons, skip checks, and restart registration.

## Nodes and the Applier boundary

The runtime is independent of the concrete node type. `Composition<TNode>` works
with the node type selected by the host and an `IApplier<TNode>` implementation.
That node could be a terminal UI object, a native MAUI view, or an object in
your own tree. The runtime tracks its identity and position; the adapter defines
its properties, child storage, layout, and rendering.

Composable components describe nodes through
`Composables.ComposeNode(factory, updater)`, optionally with child content.
The factory supplies a new node when one is needed. The updater describes how
to apply current values to that node. Factories and updates are recorded during
computation and executed when changes are applied.

For the target tree, the runtime's job is to calculate the difference and send
commands to the **Applier**. It derives those commands from matched composition
groups and recorded node updates. The adapter supplies the concrete meaning of
inserting a child, moving it, removing it, or applying a property update.

When composable code runs again, it may describe the same nodes with different
property values. The runtime retains the existing node instances and sends their
recorded updates to the Applier. When content appears or disappears, it sends
insertion or removal operations. When keyed content changes order, it can move
existing nodes instead of creating replacements. The Applier implements these
operations for the host's concrete node type.

```mermaid
flowchart LR
    G[Generated composable code] --> C[Match against retained composition]
    C --> D[Compute change operations]
    D --> A[Applier]
    A --> N[Target node tree]
```

The batch also updates the runtime's own groups and slots. Their structure
records execution and memory; the node tree represents the target UI. A group
can exist without creating a node.

### Applying node changes

`IApplier<TNode>` defines the operations through which the runtime navigates
and modifies the target tree:

| Operations | Purpose |
| --- | --- |
| `Current`, `Down`, `Up` | Navigate the active parent/node while applying changes. |
| `InsertTopDown`, `InsertBottomUp` | Notify the adapter of an insertion at the appropriate stage. |
| `Remove`, `Move`, `Clear` | Change the structure of the target tree. |
| `Apply` | Execute a recorded update against the current node. |
| `OnBeginChanges`, `OnEndChanges` | Mark the boundaries of a change batch. |

An applier implements insertion in the stage appropriate to its tree; the other
insertion callback can be a no-op. Node types must be compatible with the host's
tree. Updaters should apply current values without rebuilding unrelated children.

For reusable content, the runtime calls `IApplier.Reuse()` before updating a
retained lifecycle-capable node. Its default implementation forwards
`IComposeNodeLifecycleCallback.OnReuse()` to the current node; an override is
responsible for forwarding that callback.

Compare [TuiApplier](../src/DotNetCompose.Tui/TuiApplier.cs) and
[MauiApplier](../src/DotNetCompose.Maui/Composition/MauiApplier.cs) for concrete
implementations.

## Computing versus applying

`SetContent` computes and applies the initial content. A custom host can separate
the stages:

- `ComposeContent(content)` computes a pending change set.
- `ApplyChanges()` commits the pending batch to snapshot state and nodes.
- `DiscardChanges()` abandons a pending batch.
- `Recompose()` computes an update when observed state has invalidations;
  when it returns `true`, the host must apply the changes.

A `Recomposer` handles scheduled recomposition and application for registered
compositions. A manually driven host must deliver snapshot notifications and
drive those stages itself. Pending batches must be applied or discarded before
another computation. A batch belongs to its composition and cannot be replayed.

If application fails and the composition becomes faulted, dispose it and recreate
the host composition. Do not continue applying changes to that instance.

## Built-in composition functions

These functions expose composition memory, state, and identity to user code.
The examples below are authoring C#: their `[Composable]` methods run through
the generated entry points described in the [Source Generator guide](source-generator.md).
They demonstrate runtime behavior without requiring a particular UI adapter.

### Remembering values and state

`Composables.Remember(key, creator)` stores a value at the current composition
position. On the first visit, it calls `creator` and keeps the result in a slot.
On later visits with an equal key, it returns that result. A changed key causes
the creator to run again and replaces the stored value.

For example, this method keeps a mutable text buffer for the current document:

```csharp
using System.Text;
using DotNetCompose.Runtime;

public static partial class DraftExample
{
    [Composable(ComposableMode.Inline)]
    public static StringBuilder Draft(string documentId)
    {
        return Composables.Remember(documentId, () => new StringBuilder());
    }
}
```

`Inline` makes `Draft` a helper that uses its caller's composition context,
without creating its own restartable scope. See [composable modes](source-generator.md#composable-modes).

At a retained call position, passing `"notes"` creates one buffer; visiting it
again with `"notes"` retrieves the same buffer, including any text added to it.
Passing `"tasks"` creates a new, empty buffer. Two separate calls with `"notes"`
still keep separate buffers: the key is compared at each position, rather than
used as a lookup in a global dictionary. Removing the content drops its remembered
value; inserting it again starts a new lifetime.

Remembering a buffer does not make its contents observable. Calling
`StringBuilder.Append` does not notify the runtime or schedule recomposition.
For values whose changes should be observed, use snapshot state:

```csharp
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

public static partial class StateExample
{
    [Composable(ComposableMode.Inline)]
    public static SnapshotMutableState<int> Count(int initialValue)
    {
        return Composables.RememberState(initialValue);
    }
}
// OR
public sealed class CounterModel
{
    public SnapshotMutableState<int> Count { get; } =
        Composables.CreateMutableState(0);
}
```

`RememberState(initialValue)` remembers a snapshot state object. Reading its
`Value` during composition registers a dependency; changing `Value` can invalidate
the observing scope. If `StateExample.Count(0)` first creates state and an event
later sets its `Value` to `5`, the next visit retrieves the state containing `5`.
Passing a different `initialValue` at that position does not reset it: internally,
`RememberState` uses a constant remember key. To recreate state when an input
changes, pass that input as the key to `Remember` and create the state with
`CreateMutableState` inside its creator.

`CreateMutableState(value)` creates observable state but does not remember it.
The `CounterModel` instance above owns and retains its state through its property.
Calling `CreateMutableState` directly on each execution of a composable body
would instead create fresh state each time. Both creation functions use structural
equality by default, so assigning an equivalent value does not report a change;
`CreateMutableState` also accepts a custom mutation policy.

### Keyed content

`Composables.Key(key, content)` gives a region of content an explicit identity.
It is useful when repeated content can change order. This helper returns a
dictionary of remembered selection states so their identity can be inspected
without using any UI components:

```csharp
using System.Collections.Generic;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

public static partial class KeyExample
{
    [Composable(ComposableMode.Inline)]
    public static Dictionary<string, SnapshotMutableState<bool>> Selections(string[] itemIds)
    {
        var result = new Dictionary<string, SnapshotMutableState<bool>>();
        foreach (var itemId in itemIds)
        {
            Composables.Key(itemId, () =>
            {
                var selected = Composables.RememberState(false);
                result.Add(itemId, selected);
            });
        }
        return result;
    }
}
```

Call this helper from composable content, passing `["a", "b"]`, and store its
result as `states`. The dictionary is new each time, but its state objects belong
to the keyed regions. After application, an event can set `states["a"].Value` to `true`.
If a later pass supplies `["b", "a"]`, the keyed regions move while retaining
their state:

| Pass | Order | State for `"a"` | State for `"b"` |
| --- | --- | --- | --- |
| First | `a`, `b` | New object, `false` | New object, `false` |
| After selecting `a` | `a`, `b` | Same object, `true` | Same object, `false` |
| After reordering | `b`, `a` | Same object, `true`, now second | Same object, `false`, now first |

The dictionary exposes the result for inspection; the composition's `Key`
regions retain the state objects across passes. Without explicit content keys, state
is associated with repeated positions and can end up attached to a different
item after a reorder. Removing `"a"` removes its region and remembered state;
adding it again creates fresh state. Use keys that remain equal for an item and
distinguish it among siblings in the same repeated region. Here, "stable key"
means consistent identity, separate from the generator's type-based skip rules.

`Key` identifies an entire region that can move, including its slots and nodes.
The key in `Remember` determines when one stored value at an existing position
must be replaced. It does not give that enclosing region a movable identity.

### Composition locals

A composition local passes a value down a region of composition without adding
a parameter to every intermediate method. Providers establish a local
**environment**: the values visible to descendants in that region of composition.
This environment belongs to the composition's structure; the host's application
context determines where its code executes. Declare the local once with
`CompositionLocalOf(defaultFactory)`, supply a value with
`CompositionLocalProvider(local.Provides(value), content)`, and read the nearest
provided value with `local.Current()`.

This example supplies a language to nested content:

```csharp
using DotNetCompose.Runtime;

public static partial class LocalExample
{
    public static readonly ProvidableCompositionLocal<string> LocalLanguage =
        Composables.CompositionLocalOf(() => "en");

    [Composable(ComposableMode.ReadOnly)]
    public static string Language()
    {
        return LocalLanguage.Current();
    }

    [Composable]
    public static void Content()
    {
        var defaultLanguage = Language(); // "en"

        Composables.CompositionLocalProvider(LocalLanguage.Provides("fr"), () =>
        {
            var outerLanguage = Language(); // "fr"

            Composables.CompositionLocalProvider(LocalLanguage.Provides("de"), () =>
            {
                var innerLanguage = Language(); // "de"
            });

            var restoredLanguage = Language(); // "fr"
        });
    }
}
```

The local declaration is shared, but its provided value is scoped to content.
`Language` is a `ReadOnly` query, so its reads belong to the surrounding scope.
The nested provider overrides `"fr"` with `"de"` only inside its callback.
Outside that callback, readers still see `"fr"`; outside both providers, they
use the lazily created default `"en"`. `ProvidesDefault(value)` supplies a value
only if no ancestor provider already supplies that local. The declaration's
default factory does not count as an ancestor provider.

With `CompositionLocalOf`, reads are observed through snapshot state. When a
provider's value changes, the scopes that read that value are invalidated.
`StaticCompositionLocalOf(defaultFactory)` uses the same provider and read APIs
but does not track individual reads. Changing its provided value makes the
provider's subtree execute again, including content that did not read the local.
It is suitable for values that rarely change; "static" does not mean immutable.

Locals distribute values; they do not automatically observe mutations inside an
ordinary object supplied as a value. Use snapshot state for the object's
observable properties when those changes should drive recomposition.

## Effects and lifetime

A composable body can run repeatedly or be skipped. Starting a request or adding
an event subscription every time the body runs can repeat work unintentionally.
Effects let that work belong to the lifetime of content in composition.

For example, the following helper tracks ticks from an ordinary C# event source.
`TickSource` raises events, and `TickSubscription` attaches one handler and
implements `IDisposable` to detach it. Both are application objects, independent
of any UI adapter:

```csharp
using System;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

public sealed class TickSource
{
    public event Action? Tick;

    public void Raise()
    {
        Tick?.Invoke();
    }
}

public sealed class TickSubscription : IDisposable
{
    private readonly TickSource source;
    private readonly Action handler;

    public TickSubscription(TickSource source, Action handler)
    {
        this.source = source;
        this.handler = handler;
        source.Tick += handler;
    }

    public void Dispose()
    {
        source.Tick -= handler;
    }
}

public static partial class EffectExample
{
    [Composable(ComposableMode.Inline)]
    public static SnapshotMutableState<int> TrackTicks(TickSource source)
    {
        var count = Composables.RememberState(0);
        Composables.DisposableEffect(source,
            () => new TickSubscription(source, () => count.Value++));
        return count;
    }
}
```

Composable content calls `TrackTicks(source)` and can read the returned `Value`
to display it. The host or application owns the source and calls `Raise()` on
the host's owning context. On first successful application, the effect creates
one subscription. A tick increments the retained state, invalidating content
that read it. Re-executing with the same source does not add another handler.

Replacing the source changes the effect key: the previous subscription is
disposed, then a new one is created for the new source. The remembered counter
itself stays at its existing position. Removing the containing content disposes
the subscription too. This ties the resource's lifetime to composition without
subscribing on every execution of a method body.

| API | Lifetime |
| --- | --- |
| `SideEffect(Action)` | Publishes an action after a successful apply for a pass that records it. |
| `DisposableEffect(key, Func<IDisposable>)` | Sets up a resource when remembered and disposes it on key change or removal. |
| `DisposableEffect(key, Action)` | Runs the action as cleanup on key change or removal; this overload is not setup. |
| `LaunchedEffect(key, Func<CancellationToken, ValueTask>)` | Starts async work when remembered and requests cancellation on key change or removal. |

Cancellation is cooperative: use the token, and account for an old task still
finishing when a replacement starts. Ordinary remembered objects are not
automatically disposed. ViewModels own the lifetime of work they start
independently of composition. Dispose compositions and hosts on their owning
context to release their node and effect resources.

## References and targets

The runtime targets `netstandard2.1` and `net9.0`. Building this checkout requires
the SDK selected by [global.json](../global.json).

When writing composables in a consumer project, reference both the runtime and
the [Source Generator as an analyzer](source-generator.md#project-references).
The analyzer is needed in each project containing authoring composables.

## A custom host

Your host supplies:

1. A node type and an `IApplier<TNode>` implementation.
2. A sequential application `SynchronizationContext`.
3. A `Recomposer` and a `Composition<TNode>`.
4. Generated composable content, input handling, and rendering/layout.
5. Disposal on the owning application context.

The following lifecycle fragment belongs inside your host. `MyNode`, `applier`,
`uiContext`, and `content` represent the node type, applier, application context,
and generated `ComposableAction` supplied by that host:

```csharp
using var recomposer = new Recomposer(uiContext);
using var composition = new Composition<MyNode>(applier, recomposer);

composition.SetContent(content);

// Keep the host's event loop running here, on its owning application context.
// Dispose the composition before the recomposer when the host shuts down.
```

These types are in `DotNetCompose.Runtime.Composer`; `ComposableAction` is in
`DotNetCompose.Runtime`. A plain `new SynchronizationContext()` is rejected:
the host must provide a dispatcher that executes work sequentially on its
application context. Composition operations cannot be reentered.

For a concrete host implementation, read
[TuiApplication](../src/DotNetCompose.Tui/TuiApplication.cs) and
[MauiComposeView](../src/DotNetCompose.Maui/Composition/MauiComposeView.cs).
