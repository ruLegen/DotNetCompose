using DotNetCompose.Runtime;

namespace TestNs;

public static partial class TestClass
{
    private static readonly ProvidableCompositionLocal<int> LocalNumber =
        Composables.CompositionLocalOf(() => -1);
    private static readonly ProvidableCompositionLocal<string> LocalText =
        Composables.StaticCompositionLocalOf(() => "default");

    [Composable]
    public static void Content(int value)
    {
        Composables.CompositionLocalProvider(LocalNumber.Provides(value), () => Read());
        Composables.CompositionLocalProvider(
            new ProvidedValue[] { LocalNumber.Provides(value), LocalText.Provides("text") },
            () => Read());
    }

    [Composable]
    private static void Read()
    {
        _ = LocalNumber.Current();
        _ = LocalText.Current();
    }
}
