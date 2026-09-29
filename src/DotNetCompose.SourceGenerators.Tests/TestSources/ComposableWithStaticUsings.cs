using DotNetCompose.Runtime;
using static DotNetCompose.Runtime.Composables;

namespace TestNs;

public static partial class TestClass
{
    [Composable]
    public static void WithStaticUsing(int argInt)
    {
        int r = Remember<int>(0, () => 3);
    }
}
