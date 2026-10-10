# DotNetCompose documentation

To try the project, start with [Getting Started](getting-started.md), then run
a sample or [create your own TUI counter](tui.md#project-setup).

To understand it, read [Core concepts](concepts.md) →
[Source Generator basics](source-generator.md#basic-reading-path) →
[Runtime](runtime.md) → [advanced generator topics](source-generator.md#advanced-reading-path).
You only need C# knowledge to begin; the guides introduce the Compose model.

## Choose your path

| I want to… | Read |
| --- | --- |
| Try an existing application | [Getting Started](getting-started.md) |
| Follow a state change through composition to UI updates | [Core concepts](concepts.md) |
| Build a terminal interface | [TUI](tui.md) |
| Add composable UI to a MAUI app | [MAUI](maui.md) |
| Integrate my own node tree | [Runtime](runtime.md) |
| Understand generator rules, modes, and their generated output | [Source Generator](source-generator.md) |
| Diagnose a build or host problem | [Troubleshooting](troubleshooting.md) |

## Advanced topics

- [Generated code](advanced/generated-code.md): authoring methods, generated entry
  points, parameter tracking, and restart scopes.
- [Diagnostics and Hot Reload](advanced/diagnostics-hot-reload.md): build
  properties with on/off examples, argument buffers, source mappings, and known limitations.

## Examples and development

- [Terminal task editor](../src/DotNetCompose.Tui.Sample/README.md)
- [MAUI Windows application](../src/DotNetCompose.Maui.Sample/README.md)

These guides describe the code in this checkout. The project is experimental;
check the source and examples when working from a different revision. Shared
concepts live here, while module READMEs provide short entry points.

[Back to the project](../README.md)
