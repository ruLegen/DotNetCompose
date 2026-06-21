using DotNetCompose.Runtime;

namespace TestNs;

public class MyIntProvider : IDefaultValueProvider
{
    public static int Value => 42;
}

public static partial class TestClass
{
    [Composable]
    public static void WithDefault(
        [Default<MyIntProvider>] int x = default, string nonDefault, int someNondefaultInt, [Default<MyIntProvider>] int someOther = default)
    {
        Use(x);
    }

    [Composable]
    public static void Caller(int y)
    {
        WithDefault(x: default, "", 3, default);
        WithDefault(x: default, "", 3, 4);
        WithDefault(x: default, "", 3);

        WithDefault(y, "", 3);
        WithDefault(y, "", 3, default);
        WithDefault(y, "", 3, 4);

        WithDefault(x: 3, someOther: default, "bb", 3);
        WithDefault(y);
        WithDefault(default);
    }

    private static void Use(int v) { }
}
