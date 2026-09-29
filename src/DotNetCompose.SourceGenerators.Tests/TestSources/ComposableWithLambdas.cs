using DotNetCompose.Runtime;
using System;

namespace TestNs;

public static partial class TestClass
{
    [Composable]
    public static void MainMethod(int argInt)
    {
        int local = argInt;
        ComposableTest(3, i =>
        {
            int x = i + local;
        });
    }

    [Composable]
    private static void ComposableTest(int i, [Composable] Action<int> action)
    {
        action(i);
    }
}
