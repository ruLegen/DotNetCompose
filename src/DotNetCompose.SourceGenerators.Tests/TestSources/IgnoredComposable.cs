using DotNetCompose.Runtime;

namespace TestNs;

public static partial class TestClass
{
    [Composable]
    [ComposableIgnore]
    public static void Ignored()
    {
    }

    [Composable]
    public static void NotIgnored()
    {
    }
}
