using DotNetCompose.Runtime;

namespace TestNs;

public class MyIntProvider : IDefaultValueProvider
{
    public static int Create() => 42;
}

public static partial class TestClass
{
    [Composable]
    public static void WithDefault(
        string nonDefault, int someNondefaultInt, [Default<MyIntProvider>] int x = default, [Default<MyIntProvider>] int someOther = default)
    {
        Use(x);
    }

    [Composable]
    public static void Caller(int y)
    {
        WithDefault("", 3);
        WithDefault("", 3, default);
        WithDefault("", 3, y, default);
        WithDefault(nonDefault: "bb", someNondefaultInt: 3, someOther: default);
        WithDefault("", 3, x: y);
    }

    private static void Use(int v) {
    }
}
