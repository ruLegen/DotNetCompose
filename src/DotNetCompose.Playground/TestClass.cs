using DotNetCompose.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCompose.Playground
{
    internal partial class TestClass
    {
        static class __StoredLambdas
        {

        }
        /*
                [Composable]
                public static void NestedIfs(int i, Action regularAction, [Composable] Action<int, IComposeContext> action)
                {
                    if (i == 0)
                    {
                        int t = 0;
                        if (i == 1)
                        {
                            t = 1;
                            SomeNonComposableFunction();
                        }
                    }
                    else if (i == 2)
                    {
                        if (i == 3)
                        {
                            i = 3;
                        }
                    }
                    else
                    {

                    }
                }
                [Composable]
                public static void Add1AndCallIfEven(int i, [Composable] Action<int> action)
                {
                    if ((i + 1) % 2 == 0)
                        action?.Invoke(2);
                }


                /// <summary>
                /// <see cref="DefInt_Gen(int)"/>
                /// </summary>
                [AttributeUsage(AttributeTargets.Parameter)]
                class DefIntAttribute(int i = 3) : Attribute
                {
                }

                public const int DefInt = default;
                public static int DefInt_Gen(int i = 3) => i + 3;

                public static class Builder
                {
                    public static ComposableAction BuildApp1 = BuildApp;
                    private static void BuildApp(IComposeContext composeContext, int changed)
                    {
                    }
                    public static void App(int someVal, int o = default, ComposableAction? action = null,
                        IComposeContext __ctx = default!, int __changed = 0, int __changed2 = 0, int defaults = 0, int defa = 0)
                    {

                    }

                    public static void v(Delegate d){
                        }

                }

                /// <summary>
                /// Direct ComposableFunctionCall
                /// ComposableFunction as a function argumnet
                /// InlineComposableLambda as a function argumnet
                /// </summary>
                /// <param name="someVal"></param>
                /// <param name="o"></param>
                /// <param name="action"></param>
                [Composable]
                public static void App(int someVal, int o = default, [Composable] Action<int>? action = null)
                {
                    int d = o;
                    for (int i = 0; i < someVal; i++)
                    {
                        Add1AndCallIfEven(i, (someVal) => { });
                        if (i == 0)
                        {
                            Add1AndCallIfOdd(i, () => { });
                        }
                        else if (i == 1)
                        {
                            Add1AndCallIfOdd(i, () => { });
                        }
                        action?.Invoke(2);
                    }

                    foreach (var i in Enumerable.Range(0, 10))
                    {
                        Add1AndCallIfEven(i, (someVal) => { });
                        if (i == 0)
                            Add1AndCallIfOdd(i, () => { });
                    }
                }


                [Composable]
                public static void Add1AndCallIfOdd(int i, [Composable] Action action)
                {
                    if ((i + 1) % 2 != 0)
                        action?.Invoke();
                }


                [Composable]
                public static void Inner([Composable] Action action)
                {
                    Debug.WriteLine("Inner");
                    action.Invoke();
                }

                [Composable]
                public static void Inner2([Composable] Action action)
                {
                    Debug.WriteLine("Inner2");
                    action.Invoke();
                }

        */
        //public class CLa
        //{
        //    Delegate d = null;

        //    public CLa()
        //    {
        //    }
        //    public void Compoosss<T1, T2>(T1 t1, T2 t2, IComposeContext composeContext, int chaged)
        //    {
        //        ComposableAction<T1,T2>? typed = d as ComposableAction<T1,T2>;
        //        if (typed == null)
        //            throw new Exception("Internal error");

        //        typed.Invoke(t1,t2,composeContext,chaged);
        //    }
        //    public static CLa Get() => new CLa();
        //}
        //private void Test(ComposableAction<int, int> action)
        //{

        //}
        //private void TT()
        [Composable]
        private static void EmptyComposable(int argInt)
        {
            int localInt = 3;
            //var r = ComposeHelpers.GetLambda2(null, 2, () => (int i) => { }).Invoke;
            ////var r = ComposeHelpers.GetLambda2(null, 2, () => ((ComposableAction<int,int>)() => { })).Invoke;
            // SomeNonComposableFunction(@__xcvxcv);
            ComposableTest(3, i =>
            {
                int innerInt = localInt;
                ComposableTest(123123);
            });

            //ComposableTest(3,);
            //ComposableTest(3, ComposableTest);

        }
        private static void SomeNonComposableFunction()
        {
        }

        [Composable]
        private static void ComposableTest(int i)
        {

        }
        [Composable]
        private static void ComposableTest(int i, [Composable] Action<int> action)
        {
            EmptyComposable(i);

            action.Invoke(i);
            action(i);
            action?.Invoke(i);
        }
    }
}
