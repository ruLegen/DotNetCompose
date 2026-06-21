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
        [Default<MyIntProvider>] int x = default)
    {
        Use(x);
    }

    [Composable]
    public static void Caller(int y)
    {
        WithDefault(y);
        WithDefault(default);
        WithDefault();
    }

    private static void Use(int v) { }
}
