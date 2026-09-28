# DotNetCompose.Maui

`MauiComposeView` hosts a `Composition<MauiNode>` in a MAUI page. Call `SetContent` with a generated composable builder. The composition is created when the host loads, disposed when it unloads, and created again on a later load. Recomposition is posted through the MAUI dispatcher.

`MauiUi.NativeView` creates one MAUI `View` with `factory`, invokes `update` when the composable updates, and calls `onRelease` when the node leaves the composition. MAUI owns any children inside that view. Read snapshot state in the composable and pass its value to `update` so that the state read is tracked for recomposition.

`MauiUi.Row`, `Column`, and `Box` accept composable children. Their `content` callback is the last argument. `Modifier` is an immutable ordered chain. Layout and drawing elements wrap native views in MAUI layouts and `GraphicsView` layers, so `Padding(8).Background(color)` paints inside the padding while `Background(color).Padding(8)` paints outside it.

`MauiUi.Canvas` is one `GraphicsView` backed node. Its drawing callbacks observe snapshot reads and invalidate the view when those values change. `DrawWithContent` controls whether and how often the canvas `onDraw` callback runs. It cannot be applied to a native `View`, because MAUI has no common `ICanvas` draw operation for an arbitrary control.

The first runnable sample targets Windows at `src/DotNetCompose.Maui.Sample`. The library itself targets `net10.0` without a platform-specific target framework. The sample includes native `Button` and `Label` controls, a canvas whose color and size change with state, and a button that unloads and reloads the composition host.
