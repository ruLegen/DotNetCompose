# DotNetCompose.SourceGenerators

An incremental Roslyn generator and analyzer for `[Composable]` methods. Static
entry points live under `Builders`; instance entry points are generated overloads
on the original type.

Targets `netstandard2.0` and uses Roslyn 4.14.0. Reference it as an analyzer in
each project that declares composables; use a compatible compiler/IDE host and
the SDK selected by [global.json](../../global.json) for this checkout.

- [Core concepts](../../docs/concepts.md): the ideas behind composition and state.
- [Generator basics](../../docs/source-generator.md#basic-reading-path): references, rules, and entry points.
- Continue with [Runtime](../../docs/runtime.md), then
  [advanced generation](../../docs/source-generator.md#advanced-reading-path): modes and defaults.
- [Generated code](../../docs/advanced/generated-code.md): transformation example.
- [Diagnostics and Hot Reload](../../docs/advanced/diagnostics-hot-reload.md).
- [Shipped rules](AnalyzerReleases.Shipped.md) / [Unshipped rules](AnalyzerReleases.Unshipped.md).
- [Tests](../DotNetCompose.SourceGenerators.Tests/).

[Documentation index](../../docs/README.md) · [Project overview](../../README.md)
