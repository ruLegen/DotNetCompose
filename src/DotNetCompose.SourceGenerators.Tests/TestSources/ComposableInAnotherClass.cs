using DotNetCompose.Runtime;

namespace TestNs;

public static partial class TestClassA
{
    [Composable]
    public static void MethodA()
    {
    }
}

public static partial class TestClassB
{
    [Composable]
    public static void MethodB()
    {
    }
}
