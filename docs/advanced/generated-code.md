# Generated code

The source generator keeps authoring code and generated composition entry points
separate. This example was checked against the current generator in a Release
compilation with `DotNetComposeGenerateDiagnostics=false`. Instrumentation and
source-mapping directives are omitted from the excerpts below.

## Authoring code

The empty leaf keeps the example focused on the composition protocol:

```csharp
using DotNetCompose.Runtime;

public static partial class GenerationDemo
{
    [Composable]
    public static void Leaf(int value)
    {
    }

    [Composable]
    public static void Parent(int value)
    {
        Leaf(value);
    }
}
```

The generator adds `GenerationDemo.Builders.Leaf` and
`GenerationDemo.Builders.Parent`. Their original arguments are followed by a
composer context, change-state flags, and default-argument flags. These static
entry points can be used where a host expects a generated composable delegate.
Instance methods instead get overloads on the original type.

## Generated leaf

This method belongs inside the generated `GenerationDemo.Builders` type:

```csharp
public static void Leaf(
    int value,
    DotNetCompose.Runtime.Composer.IComposerContext __ctx,
    DotNetCompose.Runtime.ComposableArgumentsState __changed = default,
    DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState = default)
{
    try
    {
        __ctx.StartRestartableGroup(1919601813);
        byte __value_state = __changed[0];
        if (__value_state == DotNetCompose.Runtime.ComposableArgumentsState.Uncertain)
        {
            __value_state = __ctx.Changed(value)
                ? DotNetCompose.Runtime.ComposableArgumentsState.Different
                : DotNetCompose.Runtime.ComposableArgumentsState.Same;
        }

        if (!__changed.IsForced
            && (__value_state == DotNetCompose.Runtime.ComposableArgumentsState.Same
                || __value_state == DotNetCompose.Runtime.ComposableArgumentsState.Static)
            && __ctx.Skipping)
        {
            __ctx.SkipToGroupEnd();
        }
        else
        {
        }
    }
    finally
    {
        DotNetCompose.Runtime.IComposeUpdateScope? __dncScopeUpdater =
            __ctx.EndRestartableGroup(1919601813);
        if (__dncScopeUpdater != null)
        {
            byte __dncRestartChanged0 =
                DotNetCompose.Runtime.ComposableArgumentsState.NormalizeForRestart(__changed[0]);
            __dncScopeUpdater.UpdateScope(__dncRestartContext =>
            {
                Leaf(value, __dncRestartContext,
                    DotNetCompose.Runtime.ComposableArgumentsState.Forced(
                        stackalloc byte[] { __dncRestartChanged0 }), default);
            });
        }
    }
}
```

The stable integer argument makes this example eligible for comparison and
skipping. A forced restart still executes the group even if its captured
arguments have not changed. The restart callback normalizes captured argument
flags before forcing that group's execution.

## Rewritten child call

Inside the generated `Parent`, the authoring call `Leaf(value)` becomes:

```csharp
global::GenerationDemo.Builders.Leaf(value,
    __ctx: __ctx,
    __changed: new DotNetCompose.Runtime.ComposableArgumentsState(
        stackalloc byte[] { __value_state }),
    __defaultParamState: default);
```

The child receives the same context and the parent's known argument state.
For methods with optional arguments, the default mask records which arguments
were omitted. See [computed defaults](../source-generator.md#computed-default-parameters).

## Inspect your own output

Enable compiler-generated files in a consumer project:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Build the project and inspect the generator output under its `obj/` directory,
or use your IDE's generated-source view. Keep emitted files out of the project's
normal source inputs. Generated names, keys, and implementation details may
change between revisions; do not maintain generated method bodies by hand.

The `stackalloc` expressions shown here depend on the consumer's compilation
settings. Compare the [argument-buffer examples](diagnostics-hot-reload.md#argument-state-buffers)
for explicit overrides, the automatic Debug/Release choice, and the older-language
fallback. See [diagnostic instrumentation](diagnostics-hot-reload.md#runtime-diagnostic-instrumentation)
for the statements added when instrumentation is enabled and
[mode examples](../source-generator.md#composable-modes) for other composition protocols.

[Source Generator](../source-generator.md) · [Documentation index](../README.md)
