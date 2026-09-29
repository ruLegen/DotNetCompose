# DotNetCompose.Maui

`DotNetCompose.Maui` provides a small Compose style UI layer over MAUI views. It targets .NET 10; the first runnable app is Windows. Add the MAUI workload and reference both `DotNetCompose.Maui` and the `DotNetCompose.SourceGenerators` analyzer. A composable screen can use controlled state without a framework ViewModel base class:

```csharp
public sealed class CounterViewModel
{
    public SnapshotMutableState<int> Count { get; } = Composables.CreateMutableState(0);

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

composeView.SetContent(viewModel, CounterScreen.Builders.Content);
```

Create a ViewModel through MAUI DI and pass it to `SetContent`. Snapshot reads trigger recomposition. `RememberState(initialValue)` is for state local to a composable. `DisposableEffect(key, setup)` owns subscriptions and other disposable resources for the time the call remains in composition.

`TextField(value, onValueChange)` and `Switch(isChecked, onCheckedChange)` are controlled components. The caller owns their values and updates them in callbacks. 

`Modifier` is an immutable value type. `default` and `Modifier.Empty` are the same empty chain. Each call appends one persistent link, so branching a chain shares its prefix without copying the whole chain. Order matters: `Clickable(...).Padding(12)` includes the padding in the hit region; `Padding(12).Clickable(...)` excludes it. `DrawWithContent` works on `Canvas`; a native control cannot be drawn into a MAUI `ICanvas`.

`MauiTheme.Provide` uses the MAUI system theme unless `mode` is set to `Light` or `Dark`. Components use `MauiThemeData` defaults; explicit component parameters override those defaults. `NativeView` follows MAUI's normal styling and lifecycle. The Windows clickable wrapper uses a native MAUI button for keyboard focus and accessibility action; platform specific accessibility behavior outside Windows still needs implementation.

For navigation, register typed screens and nested graphs with `NavGraph.Create`, then render `MauiUi.NavHost(navigator)`. The `MauiNavigator` owns one push/pop stack. Hidden screens release their views and retain their ViewModels; leaving a graph disposes its screen and graph ViewModels after the outgoing UI is removed. Dispose the host before the navigator.

## Windows example

The sample at `src/DotNetCompose.Maui.Sample` shows a form, native `Label`, composable buttons, canvas, theme toggle, a nested editor graph with shared state, Back navigation, and host reload. With a Windows .NET 10 MAUI workload and runtime pack installed, run:

```powershell
dotnet run --project src/DotNetCompose.Maui.Sample/DotNetCompose.Maui.Sample.csproj -c Release
```

