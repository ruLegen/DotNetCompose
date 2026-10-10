# Terminal task editor sample

A runnable terminal application demonstrating snapshot state, controlled text
fields, keyed lists, selection, themes, and actions.

## Run

Install the SDK selected by [global.json](../../global.json), currently 10.0.102
with patch updates allowed. Open an interactive terminal, then run this command
from the **repository root**:

```sh
dotnet run --project src/DotNetCompose.Tui.Sample/DotNetCompose.Tui.Sample.csproj -c Release
```

No MAUI workload is needed. Dependencies are restored from NuGet when not cached.
Do not redirect or pipe input/output.

## Explore

- Use **Tab** / **Shift+Tab** to move focus and **Enter** to activate a button.
- Enter a task in the first text field and choose **Add**.
- Type in the filter field to change the visible list.
- Select a task and choose **Delete**, or **Shuffle** to reverse item order.
- **ClearAll** is displayed when the total task count is odd.
- Press **Esc**, **Ctrl+C**, or **Ctrl+Q** to exit.

[Program.cs](Program.cs) contains the screen and state.
The entry point passes `TaskEditor.Builders.Content` to `TuiApplication.Run`.
The list uses each task's ID as its composition key.

## Next steps

- [TUI guide](../../docs/tui.md#project-setup): create your own screen.
- [Core concepts](../../docs/concepts.md): understand the update flow.
- [Troubleshooting](../../docs/troubleshooting.md#tui-requires-an-interactive-terminal).

[Documentation index](../../docs/README.md) · [Project overview](../../README.md)
