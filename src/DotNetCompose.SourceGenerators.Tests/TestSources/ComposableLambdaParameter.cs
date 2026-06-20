using DotNetCompose.Runtime;
using System;

namespace TestNs;

public static partial class TestClass
{
    [Composable]
    public static void WithLambda(int i, [Composable] Action<int> action)
    {
    }
}
