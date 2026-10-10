# Source generator

`DotNetCompose.SourceGenerators` runs inside the C# compiler. It reads methods
marked with `[Composable]` and adds code that connects them to the composition
runtime. It uses Roslyn, the .NET compiler platform, and works incrementally so
the compiler can reuse generation work when inputs have not changed.

## Basic reading path

Read [Core concepts](concepts.md) first. Then follow [project references](#project-references),
[authoring rules](#authoring-rules), [call transformation](#how-calls-are-transformed),
and [entry points](#static-and-instance-entry-points). These sections are enough
to write ordinary composables and understand how a host invokes them.
Continue with [Runtime](runtime.md) before the advanced modes and computed defaults
later on this page. [Generated code](advanced/generated-code.md) provides a full
technical example when you want to inspect the complete protocol.

## Project references

There are no published DotNetCompose NuGet packages yet. Clone the repository
and connect the runtime and generator through project references.

In a consumer project next to the library projects under `src/`, add:

```xml
<ItemGroup>
  <ProjectReference Include="../DotNetCompose.Runtime/DotNetCompose.Runtime.csproj" />
  <ProjectReference Include="../DotNetCompose.SourceGenerators/DotNetCompose.SourceGenerators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Adjust the paths when your project is elsewhere. Add the analyzer reference to
each project that declares composables. The generator targets `netstandard2.0`
and uses Roslyn 4.14.0; the compiler or IDE host must be compatible with it.
Use the SDK in [global.json](../global.json) when building this checkout.

## Authoring rules

- Declare the containing type `partial`, including containing types for nested
  declarations.
- Use a block body for each composable method, and blocks for `if`/`else`,
  `for`, and `foreach` bodies.
- Call composables only from composable code. Pass a generated delegate at the
  boundary to a host.
- Pass a lambda for a composable callback argument, and annotate your own
  composable callback parameters with `[Composable]`.
- Async and iterator composable methods, and by-reference composable parameters,
  are unsupported. Put asynchronous work in callbacks or effects.
- Read-only composables can only call read-only composables. Advanced method
  modes have their own restrictions.

An ordinary event callback, such as a button's `onClick`, is different from a
composable `content` callback. The event callback updates state; the content
callback describes child content during composition.

The analyzer's [shipped rules](../src/DotNetCompose.SourceGenerators/AnalyzerReleases.Shipped.md)
and [unshipped rules](../src/DotNetCompose.SourceGenerators/AnalyzerReleases.Unshipped.md)
list diagnostic IDs. See [Troubleshooting](troubleshooting.md) for common fixes.

## How calls are transformed

Authoring code omits the **composition context**: the object that gives an
executing call access to retained memory and operations for recording nodes.
During compilation, the generator adds it as an `IComposerContext` parameter
and rewrites nested composable calls to pass it along.

For example, the [README counter](../README.md#a-small-example) calls
`CounterButton(() => count.Value++)`. Conceptually, its generated counterpart
calls the composition-aware method like this:

```csharp
// Conceptual call inside generated Content; service arguments are omitted.
Counter.Builders.CounterButton(() => count.Value++, __ctx: __ctx);
```

The generated method also receives argument-change and default-argument
information. For the default mode, it enters a composition group and registers
how to execute that region again. Groups give nested calls their own positions
in the retained record, so using one context does not merge their remembered
values. The runtime consumes this protocol during each composition pass.

A composable `content` lambda is adapted to receive the context and the two
argument-state parameters too. An ordinary `onClick` lambda keeps its original
signature: it runs in response to an event, outside the description's evaluation.
Generation happens at build time; recomposition executes the generated methods.

## Static and instance entry points

Static composables get generated methods under the containing type's
`Builders`. Instance composables get generated overloads on the original
instance type.

```csharp
using DotNetCompose.Runtime;
using DotNetCompose.Tui;

public static partial class StaticScreen
{
    [Composable]
    public static void Content()
    {
        Tui.Text("Static screen");
    }
}

public sealed partial class InstanceScreen
{
    [Composable]
    public void Content()
    {
        Tui.Text("Instance screen");
    }
}
```

Use `TuiApplication.Run(StaticScreen.Builders.Content)` for the static screen.
For the instance screen, create an instance and pass `screen.Content` to the
host; the delegate signature selects the generated overload. There is no
instance `Builders` bridge.

Inside either composable, `Tui.Text(...)` is an ordinary-looking call rewritten
by the generator. You do not pass a composer context manually in authoring code.

## Advanced reading path

The remaining sections describe choices beyond the default authoring model.
Read [groups and remembered values](runtime.md#groups-and-remembered-values) and
[state, invalidation, and skipping](runtime.md#state-invalidation-and-skipping)
before the mode examples. A **restartable scope** is a region whose state reads
and restart callback are tracked; **invalidation** marks it as needing execution.
**Skipping** preserves retained content while avoiding another execution of its
body. A **composition pass** evaluates content and produces its changes.

For debugging, you can go directly to [source stepping](#debugging-generated-composables)
or [runtime diagnostics](#inspecting-composition-diagnostics).

## Composable modes

The default `[Composable]` mode is `Restartable`. A mode selects the composition
protocol generated for a method or a composable callback parameter. These are
advanced choices; start with the default for screens and ordinary components.

| Mode | On a method | On a callback parameter | Method restart group | Method skip check |
| --- | --- | --- | --- | --- |
| `Restartable` | Yes | Yes | Yes | When eligible |
| `ReadOnly` | Yes | Yes | No | No |
| `Inline` | Yes | Yes | No | No |
| `ReadOnlyInline` | No | Yes | Not applicable | Not applicable |
| `NonSkippable` | Yes | No | Yes | No |
| `NonRestartable` | Yes | No | No | No |
| `ExplicitGroups` | Yes | No | No automatic group | No |

The following authoring fragments are methods inside the same
`public static partial class ModeExamples`, with `using DotNetCompose.Runtime;`.
They call this shared child:

```csharp
[Composable]
public static void Leaf(int value)
{
}
```

Generated excerpts below use a Release consumer compilation with
`DotNetComposeGenerateDiagnostics=false`. Signatures and debugger directives
are omitted unless relevant. The context is `__ctx`; changed argument flags are
`__changed`. Numeric group keys are actual generated values for this example,
but are implementation details. For the complete surrounding protocol, see
[Generated code](advanced/generated-code.md).

### Restartable

For skip eligibility, the current generator classifies `string` and value
types (including structs and enums) as **stable**. Other ordinary reference
types are **unstable**; composable callback parameters are handled separately.
This is a generator classification, not a guarantee that a value is immutable
or that all mutations inside it are observable. An ordinary `Action` event
handler is a reference-type parameter and can therefore prevent a method's
generated argument-based skip check. Snapshot-state reads are tracked
independently of argument stability.

Use this default for a component that should have its own restart point and,
when eligible, skip its body if its arguments and dependencies are unchanged.

```csharp
[Composable]
public static void Restartable(int value)
{
    Leaf(value);
}
```

Generated argument comparison and skip check:

```csharp
__ctx.StartRestartableGroup(5786251);
byte __value_state = __changed[0];
if (__value_state == DotNetCompose.Runtime.ComposableArgumentsState.Uncertain)
{
    __value_state = __ctx.Changed(value)
        ? DotNetCompose.Runtime.ComposableArgumentsState.Different
        : DotNetCompose.Runtime.ComposableArgumentsState.Same;
}

if (!__changed.IsForced
    && (__value_state == DotNetCompose.Runtime.ComposableArgumentsState.Same
    || __value_state == DotNetCompose.Runtime.ComposableArgumentsState.Static)
    && __ctx.Skipping)
{
    __ctx.SkipToGroupEnd();
}
```

Generated restart registration (inside `finally`):

```csharp
DotNetCompose.Runtime.IComposeUpdateScope? __dncScopeUpdater = __ctx.EndRestartableGroup(5786251);
if (__dncScopeUpdater != null)
{
    byte __dncRestartChanged0 = DotNetCompose.Runtime.ComposableArgumentsState.NormalizeForRestart(__changed[0]);
    __dncScopeUpdater.UpdateScope(__dncRestartContext =>
    {
        Restartable(value,
            __dncRestartContext,
            DotNetCompose.Runtime.ComposableArgumentsState.Forced(stackalloc byte[] { __dncRestartChanged0 }),
            default);
    });
}
```

`Changed` consumes a comparison slot when the incoming state is `Uncertain`.
The caller can forward already known flags to a child. `UpdateScope` retains a
delegate that can re-enter this method when its observed state changes.

The current generator emits skip checks only when the method has parameters and
none of its ordinary parameters are unstable. Otherwise it still creates a
restart group and forwards raw argument states, but emits no `Changed` or
`SkipToGroupEnd` check. A method with no parameters also has no generated skip
check. Runtime traversal can still bypass unaffected scopes. A forced call,
invalidated read dependency, or changed provider scope prevents normal skipping.

### ReadOnly

Use for composable queries, such as reading a composition local. A read-only
method uses its caller's context without adding groups or restart registration.
It may only call other read-only composables.

```csharp
[Composable(ComposableMode.ReadOnly)]
public static int ReadOnly(int value)
{
    return value;
}
```

Generated body:

```csharp
byte __value_state = __changed[0];
return value;
```

The argument state is forwarded, but there is no `Changed` check. State/local
reads belong to the surrounding active scope. Read-only methods cannot use
composable default providers.

### Inline

Use for composition helpers and containers whose content participates in the
surrounding execution. It adds no method restart group, skip check, or
`UpdateScope`, while retaining generated control-flow groups where needed.

```csharp
[Composable(ComposableMode.Inline)]
public static void Inline(int value)
{
    if (value > 0)
    {
        Leaf(value);
    }
}
```

Generated body:

```csharp
byte __value_state = __changed[0];
if (value > 0)
{
    __ctx.StartReplaceableGroup(208508536);
    global::ModeExamples.Builders.Leaf(value,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __value_state }),
        __defaultParamState: default);
    __ctx.EndReplaceableGroup(208508536);
}
```

The branch still has a replaceable group. `Inline` selects this composition
protocol and makes composable callback parameters on an inline method inline
too. It does not request C# method-body expansion or promise JIT inlining.
Inline composable methods cannot be virtual; inline callback parameters must
stay inside an inline call chain rather than escape to an ordinary composable
callback host.

### NonSkippable

Use when a method needs its own restart point but should execute its body each
time it is entered. This does not force traversal of the method after every
state write in the application.

```csharp
[Composable(ComposableMode.NonSkippable)]
public static void NonSkippable(int value)
{
    Leaf(value);
}
```

Generated body (debugger directives omitted):

```csharp
try
{
    __ctx.StartRestartableGroup(-1701753552);
    byte __value_state = __changed[0];
    global::ModeExamples.Builders.Leaf(value,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __value_state }),
        __defaultParamState: default);
}
finally
{
    DotNetCompose.Runtime.IComposeUpdateScope? __dncScopeUpdater = __ctx.EndRestartableGroup(-1701753552);
    if (__dncScopeUpdater != null)
    {
        byte __dncRestartChanged0 = DotNetCompose.Runtime.ComposableArgumentsState.NormalizeForRestart(__changed[0]);
        __dncScopeUpdater.UpdateScope(__dncRestartContext =>
        {
            NonSkippable(value,
                __dncRestartContext,
                DotNetCompose.Runtime.ComposableArgumentsState.Forced(stackalloc byte[] { __dncRestartChanged0 }),
                default);
        });
    }
}
```

The restart group and delegate remain. The body has no argument comparison or
skip branch; it forwards the incoming argument state to `Leaf`.

### NonRestartable

Use when a helper does not need its own restart point. Changes it observes are
handled through the surrounding restartable scope. Control-flow groups are
still generated, and its children can have their own restart points.

```csharp
[Composable(ComposableMode.NonRestartable)]
public static void NonRestartable(int value)
{
    if (value > 0)
    {
        Leaf(value);
    }
}
```

Generated body:

```csharp
byte __value_state = __changed[0];
if (value > 0)
{
    __ctx.StartReplaceableGroup(1145104647);
    global::ModeExamples.Builders.Leaf(value,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __value_state }),
        __defaultParamState: default);
    __ctx.EndReplaceableGroup(1145104647);
}
```

Like the `Inline` method above, this body has a branch group but no method restart
group or skip check. `NonRestartable` does not automatically give its callback
parameters inline semantics; that distinguishes its contract from `Inline`.

### ExplicitGroups

Use for low-level primitives that establish their own groups. The generator
still rewrites composable calls and adds context/argument parameters, but does
not insert automatic method or control-flow groups. Balance manual groups even
when an exception is thrown.

```csharp
[Composable(ComposableMode.ExplicitGroups)]
public static void ExplicitGroups(int value)
{
    var context = Composables.CurrentContext()!;
    context.StartGroup(42);
    try
    {
        Leaf(value);
    }
    finally
    {
        context.EndGroup();
    }
}
```

Generated body:

```csharp
byte __value_state = __changed[0];
var context = __ctx!;
context.StartGroup(42);
try
{
    global::ModeExamples.Builders.Leaf(value,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __value_state }),
        __defaultParamState: default);
}
finally
{
    context.EndGroup();
}
```

`CurrentContext()` becomes the generated context. The manual group remains as
written; there is no additional restart wrapper. Computed default providers are
unsupported in this mode.

### Callback modes, including ReadOnlyInline

On a composable callback parameter, the mode controls the callback's contract
and how a passed lambda is lowered. The host method's mode independently controls
its own groups and skipping. These fragments belong to `ModeExamples` too; add
`using System;` for `Action`:

```csharp
[Composable]
    public static void RegularHost([Composable] Action content)
    {
        content();
    }

[Composable]
    public static void ReadOnlyHost([Composable(ComposableMode.ReadOnly)] Action content)
    {
        content();
    }

[Composable]
    public static void InlineHost([Composable(ComposableMode.Inline)] Action content)
    {
        content();
    }

[Composable]
    public static void ReadOnlyInlineHost([Composable(ComposableMode.ReadOnlyInline)] Action content)
    {
        content();
    }

[Composable]
    public static void Callbacks(int captured)
    {
        RegularHost(() => Leaf(captured));
        ReadOnlyHost(() => ReadOnly(captured));
        InlineHost(() => Leaf(captured));
        ReadOnlyInlineHost(() => ReadOnly(captured));
    }
```

**Restartable callback:**

```csharp
global::ModeExamples.Builders.RegularHost(ComposeHelpers.GetLambda(__ctx, 0, () =>
{
    DotNetCompose.Runtime.ComposableAction a = (DotNetCompose.Runtime.Composer.IComposerContext __ctx,
        DotNetCompose.Runtime.ComposableArgumentsState __changed,
        DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState) =>
        global::ModeExamples.Builders.Leaf(captured,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __captured_state }),
        __defaultParamState: default);
    return a;
}).Invoke, __ctx: __ctx, __changed: default, __defaultParamState: default);
```

The regular captured lambda uses `GetLambda`, which retains a wrapper in a
composition group and exposes its `Invoke` delegate.

**ReadOnly callback:**

```csharp
global::ModeExamples.Builders.ReadOnlyHost(ComposeHelpers.GetReadonlyLambda(__ctx, 1, () =>
{
    DotNetCompose.Runtime.ComposableAction a = (DotNetCompose.Runtime.Composer.IComposerContext __ctx,
        DotNetCompose.Runtime.ComposableArgumentsState __changed,
        DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState) =>
        global::ModeExamples.Builders.ReadOnly(captured,
        __ctx: __ctx,
        __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __captured_state }),
        __defaultParamState: default);
    return a;
}).Invoke, __ctx: __ctx, __changed: default, __defaultParamState: default);
```

The read-only captured lambda uses `GetReadonlyLambda`. This creates a wrapper
without remembering it in a composition group. Its body may only call read-only
composables.

**Inline callback:**

```csharp
global::ModeExamples.Builders.InlineHost((DotNetCompose.Runtime.Composer.IComposerContext __ctx,
    DotNetCompose.Runtime.ComposableArgumentsState __changed,
    DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState) =>
    global::ModeExamples.Builders.Leaf(captured,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __captured_state }),
    __defaultParamState: default),
    __ctx: __ctx,
    __changed: default,
    __defaultParamState: default);
```

The lambda is passed directly as a generated composable delegate, with no
`GetLambda` wrapper. Inline callbacks must stay within an inline call chain.

**ReadOnlyInline callback:**

```csharp
global::ModeExamples.Builders.ReadOnlyInlineHost((DotNetCompose.Runtime.Composer.IComposerContext __ctx,
    DotNetCompose.Runtime.ComposableArgumentsState __changed,
    DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState) =>
    global::ModeExamples.Builders.ReadOnly(captured,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __captured_state }),
    __defaultParamState: default),
    __ctx: __ctx,
    __changed: default,
    __defaultParamState: default);
```

This combines a read-only contract with direct lambda passing. There is no
`GetReadonlyLambda` wrapper. `ReadOnlyInline` is valid on callback parameters,
but applying it to a method produces `DNC020`.

All four hosts above are themselves `Restartable`; their generated callback
parameter type is `ComposableAction`, and invoking `content()` becomes
`content(__ctx, default, default)`. The callback mode does not change the host's
method mode. Other enum values on callback parameters are rejected with `DNC020`.
Overrides must preserve composable method and callback contracts (`DNC023`).
Direct lambda passing avoids the composable wrapper; a capturing C# lambda can
still require a closure allocation.

## Computed default parameters

First read [composition locals](runtime.md#composition-locals) and the
[Inline mode](#inline). The example below obtains a default from the value
provided to the current region of composition.

Use `[Default<Provider>] T value = default` when an omitted argument needs a
computed value. The provider implements `IDefaultValueProvider` and exposes an
accessible static parameterless `T Create()` method.

`Create()` can be an ordinary method or an inline composable method. For the
latter, its containing type must be `partial`. For example:

```csharp
using DotNetCompose.Runtime;
using DotNetCompose.Tui;

public sealed partial class TitleProvider : IDefaultValueProvider
{
    [Composable(ComposableMode.Inline)]
    public static string Create()
    {
        return TitleScreen.LocalTitle.Current();
    }
}

public static partial class TitleScreen
{
    public static readonly ProvidableCompositionLocal<string> LocalTitle =
        Composables.CompositionLocalOf(() => "Untitled");

    [Composable]
    public static void Header([Default<TitleProvider>] string title = "")
    {
        Tui.Text(title);
    }

    [Composable]
    public static void Content()
    {
        Composables.CompositionLocalProvider(LocalTitle.Provides("Tasks"), () =>
        {
            Header();   // Tasks
            Header("Explicit title"); // Explicit title
        });
    }
}
```

The C# default expression is a placeholder; this example uses an empty string.
The first call obtains `"Tasks"` from the local through the default provider.
The second supplies its argument explicitly and does not request the default.
The generated default mask preserves the distinction between an omitted
argument and an explicitly supplied value, including `default`.

Default providers are evaluated within generated defaults groups and can
observe composition locals. An invalidated defaults group recomputes its values.
Providers must return a compatible type; they are not supported in
`ExplicitGroups` composables.

## Debugging generated composables

The host executes generated methods, but you normally want to debug the C# you
wrote. The generator emits `#line` directives that map user statements back to
their original file and source coordinates. With debug symbols available, place
breakpoints in the authoring method and step through it there. This also covers
user code inside composable callbacks and ordinary event/effect callbacks that
are copied into generated methods.

Generated group setup, argument tracking, and other scaffolding are marked with
`#line hidden`, so normal source stepping can pass over them. Modern C# targets
use enhanced directives containing a source span and a generated character
offset; older language versions use the simpler `#line <line> "<file>"` form.
The generated methods can still appear in the debugger's Call Stack.

For `Child` in the diagnostic example below, the generated statement has this
mapping (excerpt with the file path shortened; save the example as
`DiagnosticsExample.cs` to reproduce the source coordinates):

```csharp
#line (16, 9) - (16, 19) 20 "DiagnosticsExample.cs"
                    _ = value;
#line hidden
```

The source span points to line 16 of the authoring file. The `20` is the offset
of the statement in the generated line, not another source line number.

These mappings are independent of runtime diagnostic instrumentation. Setting
`DotNetComposeGenerateDiagnostics=false` or leaving
`DotNetComposeGenerateDiagnosticsLineNumbers=false` does not turn off the
debugger mappings. The latter property controls the line number stored in
diagnostic records, which is `0` by default; it does not control breakpoints.
See [diagnostic source line numbers](advanced/diagnostics-hot-reload.md#diagnostic-source-line-numbers)
for a generated directive example and the separate Hot Reload considerations.

## Inspecting composition diagnostics

Source stepping answers what a statement does. Runtime diagnostics show which
instrumented composable functions were entered during a composition pass,
whether their bodies executed or skipped, and how those calls nest.
The generator emits instrumentation for `Restartable` and `NonSkippable` methods
when `DotNetComposeGenerateDiagnostics` is `true` (the default). Inline and
read-only helpers do not appear as separate instrumented invocations.

A UI host creates and owns a `Composition<TNode>` together with its applier
and recomposer; see the [custom host lifecycle](runtime.md#a-custom-host).
Its application context is the dispatcher on which composition operations
run sequentially. It is distinct from the `IComposerContext` passed through
generated methods. The ready-made TUI host manages this lifecycle internally;
the following helper is intended for a host where you have access to its
composition instance.

Start a diagnostic session **before** the pass you want to inspect. This helper
receives the `Composition<TNode>` created by a host, sets generated content, and
captures the first pass. Call it on the composition's owning context in place of
the host's initial `SetContent` call:

```csharp
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Composer;
using DotNetCompose.Runtime.Diagnostics;

public static partial class DiagnosticContent
{
    [Composable]
    public static void Root()
    {
        Child(42);
    }

    [Composable]
    public static void Child(int value)
    {
        _ = value;
    }
}

public static class DiagnosticsExample
{
    public static CompositionDiagnosticsSnapshot CaptureInitialPass<TNode>(
        Composition<TNode> composition) where TNode : class
    {
        using var diagnostics = composition.StartDiagnostics();
        composition.SetContent(DiagnosticContent.Builders.Root);

        var snapshot = diagnostics.CaptureSnapshot();
        return snapshot; // Place a breakpoint here to inspect the completed pass.
    }
}
```

The content intentionally creates no UI nodes: it demonstrates the function
call tree alone. On its first pass, `snapshot.Composables` contains `Root` with
`Child` in its `Children`, both with outcome `Executed`:

```text
Root — Executed
└── Child — Executed
```

At the breakpoint, expand `snapshot` in **Locals**, or add
`snapshot.Composables` to **Watch**. Expand each invocation's `Children` to
follow the call tree. Select the `CaptureInitialPass` frame in **Call Stack**
if its locals are not currently in scope.

| Field | What to inspect |
| --- | --- |
| `Source` | Member name, source file, and diagnostic line number (`0` by default). |
| `Outcome`, `Forced` | Whether the invocation executed or skipped, and whether it was forced. |
| `Parameters` | Parameter names and change states such as `Same`, `Different`, or `Static`; these are not argument values. |
| `StateReadCount` | Recorded snapshot-state reads for the invocation. |
| `Children` | Nested instrumented invocations in this pass. |
| `DurationTicks` | Optional elapsed timing in `Stopwatch` ticks. |

The snapshot describes the **last completed diagnostic pass**, not a complete
inventory of all retained content. A skipped parent may have no recorded child
calls in that pass even though its content remains in composition. The helper
disposes its session after capture; for ongoing inspection, the host should keep
the session alive and capture again after later passes. Check `IsAvailable` and
`Status` if no completed pass has been captured.

Expand the `composition` variable in the debugger to inspect its custom view:
`Diagnostics` shows the invocation snapshot, `SlotTable` shows retained groups
and slots (with parent indices and node counts), and `PendingChanges` shows a
computed batch before it is applied. `Diagnostics` is a debugger-view entry,
not a public property you can read in C#; use the session's `CaptureSnapshot()`
in application code. The invocation tree, retained group structure, and concrete
UI node tree describe different aspects of composition.

Timings are disabled in the default session, so `DurationTicks` is normally `0`.
To enable them, replace the helper's `StartDiagnostics()` line with:

```csharp
var options = new CompositionDiagnosticsOptions();
options.Flags |= CompositionDiagnosticsFlags.Timings;
using var diagnostics = composition.StartDiagnostics(options);
```

Convert elapsed ticks to milliseconds using
`durationTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency`.
Time spent paused at breakpoints is included, so use an uninterrupted run for
timing comparisons. Generated instrumentation and session collection are separate:
if instrumentation is disabled at build time, starting a session cannot restore
the missing function records. See [runtime diagnostic instrumentation](advanced/diagnostics-hot-reload.md#runtime-diagnostic-instrumentation)
for configuration and generated `Begin`/`End` examples.

## Build settings

See [Diagnostics and Hot Reload](advanced/diagnostics-hot-reload.md) for:

- [`DotNetComposeGenerateDiagnostics`](advanced/diagnostics-hot-reload.md#runtime-diagnostic-instrumentation): instrumentation on/off.
- [`DotNetComposeGenerateDiagnosticsLineNumbers`](advanced/diagnostics-hot-reload.md#diagnostic-source-line-numbers): actual lines versus `0`.
- [`DotNetComposeUseStackAllocForArgumentStates`](advanced/diagnostics-hot-reload.md#argument-state-buffers): explicit, automatic, and fallback buffer examples.

Configure these in the consuming project; the generator DLL's own build
configuration does not choose the consumer's argument-buffer strategy.

[Documentation index](README.md)
