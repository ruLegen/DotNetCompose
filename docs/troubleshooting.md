# Troubleshooting

Start with the exact project command in [Getting Started](getting-started.md).
When reporting a problem, include the SDK version, operating system, build
configuration, full diagnostic, and a small reproduction.

## The SDK selected by global.json was not found

Run `dotnet --list-sdks` and compare it with [global.json](../global.json).
The checkout selects `10.0.102` with `latestPatch`, so a later feature band
alone does not satisfy the selection. Install the matching SDK feature band.

## Package restore fails

Check NuGet connectivity and your configured package sources. Restore needs
access to package metadata and packages that are not cached.

Release builds treat warnings as errors. `NU1900` can therefore stop a build
when NuGet cannot retrieve vulnerability data; it is distinct from a compiler
error. Fix connectivity or the package source before diagnosing the generator.

## MAUI workload or runtime is missing

Follow [MAUI setup](getting-started.md#maui-on-windows). Check
`dotnet workload list`, the SDK used by the command, and the Windows target.
The runnable sample is Windows x64, so the full solution is not a
platform-neutral first build.

If an unpackaged Windows app builds but fails during launch, check the Windows
App SDK runtime. Follow Microsoft's
[Windows App SDK deployment guide](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
for the runtime requirements.

## TUI requires an interactive terminal

Run the application directly in an interactive terminal. Piped input, redirected
output, and some IDE output panes are not interactive hosts. For automation,
use `FakeTerminalDriver` as demonstrated in the [TUI tests](../src/DotNetCompose.Tui.Tests/).

## Builders or generated overloads are missing

Check that the project declaring the composable references the generator with
`OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`.
Fix generator diagnostics first, and verify that its compiler/IDE host is
compatible with Roslyn 4.14.0.

Static methods use `Type.Builders.Method`. Instance methods use a generated
overload on the instance itself. An ordinary call outside composition cannot
replace the generated delegate passed to the host.

See [Source Generator](source-generator.md#static-and-instance-entry-points).

## Common composable diagnostics

| Diagnostic | What to check |
| --- | --- |
| `DNC001` | Replace an expression body with a block body. |
| `DNC002`–`DNC004` | Add blocks around conditional/loop bodies. |
| `DNC005`–`DNC006` | Use a lambda for a composable callback argument. |
| `DNC010` | Mark the composable's containing type `partial`. |
| `DNC012`–`DNC014` | Remove async/iterator/by-reference features from the composable signature. |
| `DNC015` | Move the call into composable content or pass a generated host delegate. |
| `DNC024` | Check the default provider's marker interface and accessible, static, parameterless `Create()`. |
| `DNC900` | Report the internal generator failure with the smallest reproducing source. |

[Authoring rules](source-generator.md#authoring-rules) and the analyzer release
files linked there describe the restrictions in more detail.

## State changes do not update the screen

Store observable values in snapshot state and read their `Value` during
composition. Reading an ordinary field or updating a control directly does not
establish that dependency.

For controlled inputs, write the callback's value into state. With your own
runtime host, verify the recomposer/dispatcher and application of pending
changes. Do not expect a different `RememberState` initial argument to replace
already remembered state.

See [state and recomposition](concepts.md#state-and-recomposition).

## Host context and disposal errors

Use MAUI hosts on the MAUI UI thread. For a custom runtime host, supply a
sequential application context and perform operations on the owning thread.
Apply or discard pending changes before computing another batch, and do not
reenter composition operations.

Dispose a faulted composition instead of trying to recover it in place.
See [Runtime](runtime.md#computing-versus-applying).

## Hot Reload fails

Read [Diagnostics and Hot Reload](advanced/diagnostics-hot-reload.md).
Generated `stackalloc` can trigger `ENC0044`; generated diagnostic line numbers
can also interfere with edits that insert or delete lines. Compiler source
mappings and diagnostic line-number literals are separate settings.

[Documentation index](README.md)
