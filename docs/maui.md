# MAUI integration

`DotNetCompose.Maui` provides a Compose-style UI layer over native MAUI views.
`MauiComposeView` hosts composable content inside an existing page. The library
targets `net10.0` and `net10.0-windows10.0.19041.0`; the runnable sample targets
Windows x64.

Start with [MAUI setup](getting-started.md#maui-on-windows) and the
[Windows sample](../src/DotNetCompose.Maui.Sample/README.md).
[Core concepts](concepts.md) covers state and recomposition;
[Runtime](runtime.md#effects-and-lifetime) explains effects and resource lifetime.

## Project setup

Use a MAUI project with the appropriate Windows target and workload. Reference
the MAUI adapter and the generator as an analyzer. For a sibling project under
`src/`:

```xml
<ItemGroup>
  <ProjectReference Include="../DotNetCompose.Maui/DotNetCompose.Maui.csproj" />
  <ProjectReference Include="../DotNetCompose.SourceGenerators/DotNetCompose.SourceGenerators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The adapter references the runtime. Adjust paths for another project location.
The [sample project](../src/DotNetCompose.Maui.Sample/DotNetCompose.Maui.Sample.csproj)
contains the complete MAUI application setup.

## A screen with an ordinary ViewModel

A ViewModel can own snapshot state without inheriting a framework base class.
In this example, `Column` arranges child views vertically. A **modifier**
describes additional layout or behavior attached to a component;
`Modifier.Empty.Padding(24)` adds padding around the column. The `spacing`
parameter controls the gap between its children.

`MauiComposeView` is the host view that connects these composable calls to a
native MAUI tree:

```csharp
using DotNetCompose.Maui;
using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;

public sealed class CounterViewModel
{
    public SnapshotMutableState<int> Count { get; } =
        Composables.CreateMutableState(0);

    public void Increment()
    {
        Count.Value++;
    }
}

public static partial class CounterScreen
{
    [Composable]
    public static void Content(CounterViewModel viewModel)
    {
        MauiUi.Column(spacing: 12, modifier: Modifier.Empty.Padding(24), content: () =>
        {
            MauiUi.Text($"Count: {viewModel.Count.Value}");
            MauiUi.Button(viewModel.Increment, content: () => MauiUi.Text("Increment"));
        });
    }
}

public static class CounterHost
{
    public static MauiComposeView Create(CounterViewModel viewModel)
    {
        var composeView = new MauiComposeView();
        composeView.SetContent(viewModel, CounterScreen.Builders.Content);
        return composeView;
    }
}
```

Resolve the ViewModel through your application's DI setup and assign the view
returned by `CounterHost.Create(viewModel)` to a page or layout on the UI thread.
Snapshot reads establish the dependencies used to schedule recomposition.

The host mounts on `Loaded` and unmounts its composition on `Unloaded`.
Composition-local state is lost on unmount; a separately retained ViewModel can
preserve its state. Dispose the host when it is permanently released.
`SetContent` does not take ownership of an externally supplied ViewModel:
the application remains responsible for its cancellation and disposal.

## Controlled inputs and effects

`TextField(value, onValueChange)` and `Switch(isChecked, onCheckedChange)`
are controlled components. The caller stores the value and updates it in the
callback.

Use `RememberState(initialValue)` for composition-local state and
`DisposableEffect(key, setup)` for composition-owned subscriptions/resources.
`LaunchedEffect` accepts asynchronous work with a cancellation token.
See [effects and lifetime](runtime.md#effects-and-lifetime), including the
difference between the two `DisposableEffect` overloads.

## Layout and modifiers

The adapter includes `Row`, `Column`, `Box`, `Surface`, scrolling, and native
view embedding. During measurement, a parent gives a child **constraints**:
the minimum and maximum width and height available to it. The child chooses
a size within those bounds; arrangement places it in the resulting space.
`Box` overlays and aligns children. Its minimum-constraint propagation option
controls whether children must also satisfy the minimum size given to the box.

A modifier chain describes transformations around a component, such as
padding, sizing, decoration, or input handling. In the example above, padding
reserves space around the column; it does not change the gap between children.

`Modifier` is an immutable value type. `default` and `Modifier.Empty` are the
same empty chain. Each call appends one persistent link, so branching a chain
shares its prefix without copying the whole chain.

Order matters: `Clickable(...).Padding(12)` includes the padding in the hit
region; `Padding(12).Clickable(...)` excludes it. Size, background, border,
drawing, and click modifiers are applied in their declared order.

`NativeView(factory, update, ...)` embeds a native control and follows MAUI's
normal styling and lifecycle. `DrawWithContent` works on `Canvas`; a native
control cannot be drawn into a MAUI `ICanvas`.

## Themes and accessibility

`MauiTheme.Provide` uses the MAUI system theme unless `mode` is set to
`Light` or `Dark`. Components use `MauiThemeData` defaults; explicit component
parameters override those defaults.

The Windows clickable wrapper uses a native MAUI button for keyboard focus
and accessibility action. Platform-specific accessibility behavior outside
Windows still needs implementation.

## Navigation

A **screen** is a piece of composable content with its ViewModel. A **route**
is a typed value identifying a destination, optionally carrying arguments.
A navigation **graph** registers which screens those routes open and how to
create their ViewModels. Nested graphs group related destinations and can
share a graph-level ViewModel. The navigator keeps the history of visited
destinations; `NavHost` displays the active destination.

Register typed screens and nested graphs with `NavGraph.Create`, then render
`MauiUi.NavHost(navigator)` from composable content. A screen registration pairs
a route type and ViewModel factory with a generated screen delegate, such as
`CounterScreen.Builders.Content`.

`MauiNavigator` owns one push/pop stack. Hidden screens release their views and
retain their ViewModels. Leaving a graph disposes its screen and graph
ViewModels after the outgoing UI is removed. Dispose the host before the
navigator.

For a complete registration example, read the sample's
[navigation graph](../src/DotNetCompose.Maui.Sample/Navigation/SampleNavigation.cs)
and [page lifecycle](../src/DotNetCompose.Maui.Sample/MainPage.xaml.cs).
They also show sharing a graph ViewModel through `NavContext`.

## Host reload

The sample's **Reload host** button calls `MauiComposeView.Reload()`; its
**Light / Dark** button updates application state. Explicit host reload is
separate from compiler/IDE Hot Reload. Read
[Diagnostics and Hot Reload](advanced/diagnostics-hot-reload.md) for build
settings and limitations.

[Documentation index](README.md)
