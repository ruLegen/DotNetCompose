using DotNetCompose.Runtime;

namespace TestNs;

public static partial class TestClass
{
    [Composable]
    public static void GenericMethod<T>(int i, T param)
    {
    }
}
