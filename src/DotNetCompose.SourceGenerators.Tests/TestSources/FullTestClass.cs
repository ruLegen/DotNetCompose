using DotNetCompose.Runtime;
using System;
using static DotNetCompose.Runtime.Composables;

namespace DotNetCompose.Playground;

public static partial class TestClass
{
    [Composable]
    public static void dd<T>()
    {
        IgnoredComposable();
        IgnoredComposable2();
    }

    [Composable]
    [ComposableIgnore]
    public static void IgnoredComposable()
    {
    }

    [Composable, ComposableIgnore]
    public static void IgnoredComposable2()
    {
    }

    [Composable]
    public static void Unstable(int i, object obj)
    {
    }

    [Composable]
    public static void Stable(int i, string stable, int g, string kkk, string sdf)
    {
    }

    [Composable]
    public static void StableTestGeneric<T>(int i, T param2)
    {
    }

    [Composable]
    public static void EmptyComposable(int argInt)
    {
        int localInt = 3;

        int rememberedFromStaticUsings = Remember<int>(0, () => 3);

        int rememberedInt = Composables.Remember(0, () => 3);
        string rememberedstring = Composables.Remember<string>(0, () => string.Empty);
        Unstable(argInt, null);
        Stable(argInt, "", 3, "", "");
        StableTestGeneric<int>(argInt, 3);
        StableTestGeneric<object>(argInt, new object());

        ComposableTest(3, i =>
        {
            int nonCaptured = i;
            ComposableTest(123);
        });
        ComposableTest(3, i =>
        {
            int nonCaptured = i;

            ComposableTest2(123, (i) =>
            {
                int someLocal = i;
            });
        });
        ComposableTest(3, i =>
        {
            int innerInt = localInt * i;
            ComposableTest(123123);
        });

        ComposableTest(3);
    }

    [Composable]
    private static void ComposableTest(int i)
    {
    }

    [Composable]
    private static void ComposableTest2(int i, [Composable] Action<int> action)
    {
        action.Invoke(i);
        action(i);
        action?.Invoke(i);
    }

    [Composable]
    private static void ComposableTest(int i, [Composable] Action<int> action)
    {
        action.Invoke(i);
        if (i == 0)
        {
            action(i);
        }
        else
        {
            action?.Invoke(i);
        }
    }
}
