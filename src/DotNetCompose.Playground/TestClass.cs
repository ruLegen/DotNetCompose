using DotNetCompose.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCompose.Playground
{
    internal partial class TestClass
    {
        [Composable]
        public static void ComposableGenericTest()
        {
            int i = 0;
            ComposableNoArg();
            ComposableArg(i, () => { }, ComposableNoArg);
        }
        //[Composable]
        //public static void ComposableGenericTest<T, K>(T arg)
        //{
        //    InnerComposable();
        //    bool l = false;
        //    if (l)
        //    {
        //        int v = 0;
        //        ComposableTest();
        //    }
        //    else
        //    {
        //        // IsComposableCall?
        //        // get args

        //        ComposableArg(() =>
        //        {
        //            if (0 == 0)
        //            {

        //            }
        //            else
        //            {
        //                string innerLambda = "InnerLabmda";
        //            }
        //        });
        //    }
        //}

        //[Composable]
        //public static void ComposableTest()
        //{
        //    InnerComposable();
        //    ComposableGenericTest<int, object>(3);
        //    NotComposable();
        //    NotComposable();
        //}

        //[Composable]
        //public static void InnerComposable()
        //{
        //    ComposableArg(() =>
        //    {
        //        SomeOtherComposable();
        //    });


        //    ComposableArg(SomeOtherComposable);
        //    ComposableArg(NotComposable);
        //}

        [Composable]
        public static void ComposableArg(int r, ComposableAction content, ComposableAction content2)
        {
            int i = 0;
            ComposableLambdaWrapper composableLambdaWrapper = new ComposableLambdaWrapper(() =>
            {
                Console.WriteLine(i);
            });
        }
        [Composable]
        public static void ComposableNoArg()
        {
        }
        //[Composable]
        //public static void SomeOtherComposable() { }


        //public static void NotComposable()
        //{

        //}
    }
}
