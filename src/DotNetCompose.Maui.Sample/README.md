# MAUI Windows sample

A runnable Windows application demonstrating composable screens inside MAUI,
ordinary ViewModels, nested navigation, native views, and drawing.

## Run

Use Windows x64, the SDK selected by [global.json](../../global.json), the
.NET 10 MAUI Windows workload, and the Windows App SDK runtime. Follow
[Windows setup](../../docs/getting-started.md#maui-on-windows) first.

Run from the **repository root**:

```powershell
dotnet run --project src/DotNetCompose.Maui.Sample/DotNetCompose.Maui.Sample.csproj -c Release
```

The project targets `net10.0-windows10.0.19041.0` with `win-x64` and uses an
unpackaged Windows application. Dependencies require NuGet access when not
cached.

## Explore

- Wait for the home screen to initialize; it displays a native MAUI `Label`,
  composable buttons, and an elapsed-time value.
- Open the editor and change the text field, decoration switch, and counter.
- Open the preview to see shared graph state and canvas drawing; use **Edit**
  or **Back** to navigate.
- Use **Light / Dark** to change the theme.
- Use **Reload host** to recreate the composition while retaining externally
  owned application/navigation state.

## Find the code

| File | Purpose |
| --- | --- |
| [MainPage.xaml.cs](MainPage.xaml.cs) | Host, theme/reload controls, and disposal order. |
| [SampleScreens.cs](Screens/SampleScreens.cs) | Composable home, editor, and preview screens. |
| [SampleNavigation.cs](Navigation/SampleNavigation.cs) | Typed routes, screen factories, and nested graphs. |
| [MauiProgram.cs](MauiProgram.cs) | MAUI application and service registration. |

The host is disposed before the navigator. ViewModels are retained by
navigation entries while their screens are hidden, and released when those
entries leave the stack.

## Next steps

- [MAUI guide](../../docs/maui.md): hosting, modifiers, themes, and navigation.
- [Core concepts](../../docs/concepts.md): state and recomposition.
- [Effects and lifetime](../../docs/runtime.md#effects-and-lifetime): composition-owned resources.
- [Diagnostics and Hot Reload](../../docs/advanced/diagnostics-hot-reload.md):
  explicit host reload versus compiler/IDE Hot Reload.

[Documentation index](../../docs/README.md) · [Project overview](../../README.md)
