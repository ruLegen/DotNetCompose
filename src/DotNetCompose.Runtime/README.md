# DotNetCompose.Runtime

The composition engine: snapshot state, remembered values, effects, composition
groups, and node changes applied through `IApplier<TNode>`.

Targets `netstandard2.1` and `net9.0`. Building this checkout requires the SDK
selected by [global.json](../../global.json). Projects declaring composables
also need the source generator as an analyzer.

- [Runtime guide](../../docs/runtime.md): integrate a custom host and node tree.
- [Core concepts](../../docs/concepts.md): state, recomposition, and identity.
- [Source Generator basics](../../docs/source-generator.md#basic-reading-path): read before the Runtime guide.
- [Tests](../DotNetCompose.Runtime.Tests/).

For a ready-made UI host, use [TUI](../../docs/tui.md) or [MAUI](../../docs/maui.md).

[Documentation index](../../docs/README.md) · [Project overview](../../README.md)
