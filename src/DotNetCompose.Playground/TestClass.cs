using DotNetCompose.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DotNetCompose.Playground.TestClass2;
using static DotNetCompose.Runtime.Composables;

namespace DotNetCompose.Playground
{
    public static partial class TestClass
    {
        public partial class Builders
        {
            public static void IgnoredComposable(IComposeContext _ctx, ComposableArgumentsState changed = default)
            {
                Console.WriteLine("Print");
            }

            public static void IgnoredComposable2(IComposeContext _ctx, ComposableArgumentsState changed = default)
            {
                Console.WriteLine("Print2");
            }
        }
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
        public static void EmptyComposable(int argInt)
        {
            int localInt = 3;
            //Composables.CurrentContext();

            int rememberedFromStaticUsings = Remember<int>(0, () => 3);

            int rememberedInt = Composables.Remember(0, () => 3);
            string rememberedstring = Composables.Remember<string>(0, () => string.Empty);
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
            // ComposableTest(3, ComposableTest);

        }
        private static void SomeNonComposableFunction()
        {
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
}
