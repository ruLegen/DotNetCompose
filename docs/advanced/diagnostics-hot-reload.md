# Diagnostics and Hot Reload

Configure generator behavior in the consuming project's `.csproj` or
`Directory.Build.props`. DotNetCompose NuGet packages are not published yet; use
[project references](../source-generator.md#project-references) from a clone.

For breakpoints and source stepping, read [Debugging generated composables](../source-generator.md#debugging-generated-composables).
For a diagnostic session example and inspecting function trees, read
[Inspecting composition diagnostics](../source-generator.md#inspecting-composition-diagnostics).

## Settings at a glance

| Property | Default | Purpose |
| --- | --- | --- |
| `DotNetComposeGenerateDiagnostics` | `true` | Generate runtime composition diagnostic instrumentation. |
| `DotNetComposeGenerateDiagnosticsLineNumbers` | `false` | Include source line numbers in that instrumentation. |
| `DotNetComposeUseStackAllocForArgumentStates` | Automatic | Select storage for argument/default-state buffers. |

Runtime composition instrumentation is separate from compiler errors such as
`DNC010`. Disabling instrumentation does not disable authoring rules.

This repository exposes the properties to the compiler in
[Directory.Build.props](../../Directory.Build.props). The source tree also contains
`buildTransitive` props for packaging infrastructure; they do not imply that
NuGet packages are available. If you reference these projects from
outside this repository through `ProjectReference`, add the following to your
consumer's project/build props so the generator can see your settings:

```xml
<ItemGroup>
  <CompilerVisibleProperty Include="DotNetComposeGenerateDiagnostics" />
  <CompilerVisibleProperty Include="DotNetComposeGenerateDiagnosticsLineNumbers" />
  <CompilerVisibleProperty Include="DotNetComposeUseStackAllocForArgumentStates" />
  <CompilerVisibleProperty Include="ProjectDir" />
</ItemGroup>
```

## Runtime diagnostic instrumentation

`DotNetComposeGenerateDiagnostics` defaults to `true`. Compare the following
settings in the consuming project:

```xml
<PropertyGroup>
  <DotNetComposeGenerateDiagnostics>true</DotNetComposeGenerateDiagnostics>
</PropertyGroup>
```

Set the value to `false` to remove runtime diagnostic instrumentation at compile
time. For all comparisons below, use this source (`Leaf` is declared on line 6):

```csharp
using DotNetCompose.Runtime;

public static partial class SettingsExample
{
    [Composable]
    public static void Leaf(int value)
    {
        _ = value;
    }

    [Composable]
    public static void Parent(int value)
    {
        Leaf(value);
    }

    [Composable]
    public static void NoArgs()
    {
    }
}
```

With `true`, the generated `Leaf` body includes the following excerpts (Release,
diagnostic line numbers disabled). Group setup, argument comparison, the body,
and restart registration are omitted here:

```csharp
global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsToken __dncDiagnostics = default;
if (global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.IsSupported)
{
    __dncDiagnostics = global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.Begin(-4660428163115803183L,
        -794224767,
        "Leaf",
        "SettingsExample.cs",
        0,
        "value");
}

global::DotNetCompose.Runtime.Diagnostics.ComposableExecutionOutcome __dncOutcome;
```

After assigning the executed/skipped outcome, the generated code reports it:

```csharp
if (global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.IsSupported
    && __dncDiagnostics.IsActive)
{
    global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.End(__dncDiagnostics,
        __dncOutcome,
        __changed.IsForced,
        stackalloc byte[] { __value_state });
}
```

With `false`, those token, outcome, `Begin`, and `End` statements are absent.
The same method still has its composition protocol, including this skip/body
fragment:

```xml
<PropertyGroup>
  <DotNetComposeGenerateDiagnostics>false</DotNetComposeGenerateDiagnostics>
</PropertyGroup>
```

```csharp
if (!__changed.IsForced
    && (__value_state == DotNetCompose.Runtime.ComposableArgumentsState.Same
    || __value_state == DotNetCompose.Runtime.ComposableArgumentsState.Static)
    && __ctx.Skipping)
{
    __ctx.SkipToGroupEnd();
}
else
{
    _ = value;
}
```

Disabling instrumentation does not disable state observation, recomposition,
restart registration, or analyzer diagnostics. Instrumentation is generated for
methods with restart groups (`Restartable` and `NonSkippable`); other method
modes do not gain a restart group when this setting is enabled. Generated calls
are guarded by the runtime's `IsSupported`/token activity checks.

## Argument state buffers

The generator tracks argument changes and omitted default arguments in temporary
buffers. You can explicitly avoid generated `stackalloc`:

```xml
<PropertyGroup>
  <DotNetComposeUseStackAllocForArgumentStates>false</DotNetComposeUseStackAllocForArgumentStates>
</PropertyGroup>
```

When unset, the setting follows the consuming compilation's optimization level:
Debug avoids explicit `stackalloc`, while Release uses it. Explicit `true`
always uses `stackalloc`; explicit `false` always avoids it. Empty or invalid
boolean values use automatic mode. The generator DLL's build configuration does
not affect this choice.

Without `stackalloc`, C# 12+ targets supporting inline arrays use collection
expressions passed directly to `Span<byte>` or `ReadOnlySpan<byte>`. This allows
the compiler to use local inline arrays without heap allocation. Older language
versions or targets use `byte[]` buffers instead. Empty buffers allocate nothing.


For the `Parent` call in the same example, the generated expressions differ as
follows. Debugger directives and surrounding code are omitted.

**Explicit `true` (Debug or Release):**

```xml
<PropertyGroup>
  <DotNetComposeUseStackAllocForArgumentStates>true</DotNetComposeUseStackAllocForArgumentStates>
</PropertyGroup>
```

```csharp
global::SettingsExample.Builders.Leaf(value,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState(stackalloc byte[] { __value_state }),
    __defaultParamState: default);
```

**Explicit `false`, C# 12+ with runtime inline-array support:**

```csharp
global::SettingsExample.Builders.Leaf(value,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState([__value_state]),
    __defaultParamState: default);
```

**Explicit `false`, C# 11 fallback:**

```csharp
global::SettingsExample.Builders.Leaf(value,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState(new byte[] { __value_state }),
    __defaultParamState: default);
```

`false` avoids an explicit generated `stackalloc`; it does not require a heap
allocation on modern targets. The C# compiler chooses the lowering of the
collection expression passed to the span-based argument-state constructor.

| Setting | Consumer optimization level | Modern target output |
| --- | --- | --- |
| Unset, empty, or invalid | Debug (unoptimized) | Collection expression |
| Unset, empty, or invalid | Release (optimized) | `stackalloc` |
| `true` | Either | `stackalloc` |
| `false` | Either | Collection expression |

The usual Debug/Release configurations select these optimization levels;
overriding the consumer's `Optimize` property changes automatic selection.
Where collection expressions are unavailable, the non-stackalloc rows use
`new byte[]` instead.

Empty buffers need no allocation in any mode. For example, the diagnostic end
call generated for `NoArgs` uses an empty read-only span:

```csharp
global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.End(__dncDiagnostics,
    __dncOutcome,
    __changed.IsForced,
    global::System.ReadOnlySpan<byte>.Empty);
```

The setting covers argument change states, default masks, restart lambdas, and
argument states reported to diagnostics. `DotNetComposeGenerateDiagnostics`
independently controls whether diagnostic instrumentation is generated.

Avoiding generated `stackalloc` removes the `ENC0044` blocker for Hot Reload;
other standard C# Hot Reload restrictions still apply. User-written
`stackalloc` is unaffected.

## Diagnostic source line numbers

Diagnostic source line numbers are disabled by default in both Debug and Release:

```xml
<PropertyGroup>
  <DotNetComposeGenerateDiagnosticsLineNumbers>false</DotNetComposeGenerateDiagnosticsLineNumbers>
</PropertyGroup>
```

Set this property to `true` to report the actual source line. Otherwise
instrumentation reports `0`, meaning an unknown line; the file path and member
name remain available. Missing, empty, or invalid boolean values are treated as
`false`. This property is independent of `DotNetComposeGenerateDiagnostics`
and does not enable instrumentation.

Debugger `#line` mappings remain enabled and retain their actual source
coordinates. The diagnostic line-number property does not disable stepping
mappings for composable bodies and callbacks.

For `Leaf` in the example above, with diagnostic instrumentation enabled:

**Line numbers disabled (`false`, the default):**

```csharp
__dncDiagnostics = global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.Begin(-4660428163115803183L,
    -794224767,
    "Leaf",
    "SettingsExample.cs",
    0,
    "value");
```

**Line numbers enabled (`true`):**

```xml
<PropertyGroup>
  <DotNetComposeGenerateDiagnosticsLineNumbers>true</DotNetComposeGenerateDiagnosticsLineNumbers>
</PropertyGroup>
```

```csharp
__dncDiagnostics = global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime.Begin(-4660428163115803183L,
    -794224767,
    "Leaf",
    "SettingsExample.cs",
    6,
    "value");
```

Only the diagnostic source-line argument changes from `0` to `6`. Both versions
still emit the following debugger mapping for the statement on source line 8
(the path reflects this verification checkout):

```csharp
#line (8, 9) - (8, 19) 20 "F:\sources\DotNetCompose\artifacts\docs-verification\GeneratorExamples\SettingsExample.cs"
```

With `DotNetComposeGenerateDiagnostics=false`, enabling line numbers generates
no `Begin`/`End` instrumentation. The debugger mapping still remains.

## Known Hot Reload limitation

Generating diagnostic line numbers can break Hot Reload after inserting or
deleting source lines. Changed line-number literals update otherwise unchanged
generated methods containing lambdas. In the reproduced case, Roslyn fails
while emitting the EnC delta with `ENC1002` and
`ArgumentException: An item with the same key has already been added` in
`DeltaMetadataWriter.GetDelta`.

A similar failure is documented in [dotnet/roslyn#77224](https://github.com/dotnet/roslyn/issues/77224);
a shared root cause has not been established. Keep
`DotNetComposeGenerateDiagnosticsLineNumbers` set to `false` for Hot Reload.
Debugger source mappings are preserved.

The MAUI sample's explicit **Reload host** action recreates its host composition;
it is separate from applying compiler/IDE Hot Reload deltas.

[Source Generator](../source-generator.md) · [Documentation index](../README.md)
