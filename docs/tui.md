# Terminal UI

`DotNetCompose.Tui` adapts the runtime to an interactive terminal. It provides
text, rows and columns, borders, buttons, text fields, keyed lists, and themes.

Start by running the [task editor sample](../src/DotNetCompose.Tui.Sample/README.md).
Read [Core concepts](concepts.md) for state and recomposition.

## Project setup

First [clone the repository and install its SDK](getting-started.md#get-the-repository-and-sdk).
No MAUI workload is required. Run the commands below from the repository root.
There are no published DotNetCompose packages yet, so this project uses local
project references.

Create a console project alongside the libraries:

```sh
dotnet new console -n CounterDemo -o src/CounterDemo -f net10.0
```

In `src/CounterDemo/CounterDemo.csproj`, keep the generated property group
(including `ImplicitUsings` and `Nullable`) and add the following item group
inside `<Project>`. These paths are relative to that project file:

```xml
<ItemGroup>
  <ProjectReference Include="../DotNetCompose.Runtime/DotNetCompose.Runtime.csproj" />
  <ProjectReference Include="../DotNetCompose.Tui/DotNetCompose.Tui.csproj" />
  <ProjectReference Include="../DotNetCompose.SourceGenerators/DotNetCompose.SourceGenerators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Replace `src/CounterDemo/Program.cs` with this complete program:

```csharp
using System;
using DotNetCompose.Runtime;
using DotNetCompose.Tui;

// Start the terminal UI host and its event loop.
// The source generator creates Builders.Content from Content below.
TuiApplication.Run(Counter.Builders.Content);

public static partial class Counter
{
    [Composable]
    public static void Content()
    {
        var count = Composables.RememberState(0);

        Tui.Column(gap: 1, content: () =>
        {
            Tui.Text($"Count: {count.Value}");
            CounterButton(() => count.Value++);
        });
    }

    [Composable]
    public static void CounterButton(Action onClick)
    {
        Tui.Button("Add", onClick);
    }
}
```

Open an interactive terminal and run:

```sh
dotnet run --project src/CounterDemo/CounterDemo.csproj -c Release
```

You should see **Count: 0** and an **Add** button. Use **Tab** to focus the button
and **Enter** to activate it: the text becomes **Count: 1**. Press **Esc** to exit.
Do not pipe or redirect input/output. For a project outside `src/`, adjust the
reference paths to its location.

`TuiApplication.Run(Counter.Builders.Content)` owns the default terminal driver,
composition, recomposer, and input/render loop. It restores the terminal when
the application exits. You can also pass a terminal driver and cancellation
token to the host.

## Components and layout

| API | Purpose |
| --- | --- |
| `Text` / `Spacer` | Display text or reserve space. |
| `Row` / `Column` | Arrange children horizontally/vertically, with a gap in cells. |
| `Border` | Wrap content in a border with an optional title. |
| `Button(label, onClick)` | Invoke a callback when activated. |
| `TextField(value, onValueChanged)` | Edit a caller-owned string. |
| `List(items, key, …, itemContent)` | Render keyed items and report selection changes. |
| `Theme(theme, content)` | Provide styles to a subtree. |

`TuiLayout` uses terminal cell dimensions. `TuiLength.Auto` sizes to content,
`TuiLength.Cells(n)` selects a fixed dimension, and `TuiLength.Fill()` shares
available space. `TuiLayout.Fill` fills both dimensions.

For example, this screen controls a text field through local state:

```csharp
using DotNetCompose.Runtime;
using DotNetCompose.Tui;

public static partial class GreetingScreen
{
    [Composable]
    public static void Content()
    {
        var name = Composables.RememberState("");

        Tui.Column(gap: 1, content: () =>
        {
            Tui.TextField(name.Value, value => name.Value = value,
                placeholder: "Your name");
            Tui.Text($"Hello, {name.Value}");
        });
    }
}
```

Host it with `TuiApplication.Run(GreetingScreen.Builders.Content)`. A text field
does not own your application value; the callback must write the new value to
the state you pass back on the next composition.

## Input and testing

Use **Tab** / **Shift+Tab** to move focus and **Enter** to activate a focused
button. **Esc**, **Ctrl+C**, or **Ctrl+Q** exits the application.

The default driver requires interactive input/output. For automated tests,
use `FakeTerminalDriver`; see the [TUI tests](../src/DotNetCompose.Tui.Tests/).
See [Troubleshooting](troubleshooting.md#tui-requires-an-interactive-terminal)
for terminal errors.

[Documentation index](README.md)
