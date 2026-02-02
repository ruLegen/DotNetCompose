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
        [Composable]
        public static void App(int someVal)
        {
            if (someVal == 2)
            {
                if (someVal < 5)
                {
                    SomeNonComposableFunction();
                }
                else
                {
                    SomeNonComposableFunction();
                }
            }
            if (someVal == 2)
            {
                if (someVal < 5)
                {
                    Add1AndCallIfEven(2, null);
                }
                else
                {
                    SomeNonComposableFunction();
                }
            }


            Add1AndCallIfEven(someVal, () =>
            {
                Inner(null);
            });

            Add1AndCallIfOdd(someVal, () =>
            {
                Inner2(null);
            });
        }

        private static void SomeNonComposableFunction()
        {
        }

        [Composable]
        public static void Add1AndCallIfEven(int i, ComposableAction action)
        {
            if ((i + 1) % 2 == 0)
                action?.Invoke();
        }

        [Composable]
        public static void Add1AndCallIfOdd(int i, ComposableAction action)
        {
            if ((i + 1) % 2 != 0)
                action?.Invoke();
        }


        [Composable]
        public static void Inner(ComposableAction action)
        {
            Debug.WriteLine("Inner");
            action.Invoke();
        }

        [Composable]
        public static void Inner2(ComposableAction action)
        {
            Debug.WriteLine("Inner2");
            action.Invoke();
        }

        #region r
        [DotNetCompose.Runtime.ComposeGeneratedAttribute, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void App_Generated(int someVal)
        {
            var var0 = DotNetCompose.Runtime.ComposeScope.GetCurrentContext();
            var0?.StartGroup(2001);
            if (someVal == 2)
            {
                if (someVal < 5)
                {
                    SomeNonComposableFunction();
                }
                else
                {
                    SomeNonComposableFunction();
                }
            }
            if (someVal == 2)
            {
                var0?.StartRestartableGroup(2004);
                if (someVal < 5)
                {
                    var0?.StartRestartableGroup(2002);
                    Add1AndCallIfEven(2, ((DotNetCompose.Runtime.ComposableAction)(null)));
                    var0?.EndRestartableGroup(2002);
                }
                else
                {
                    var0?.StartRestartableGroup(2003);
                    SomeNonComposableFunction();
                    var0?.EndRestartableGroup(2003);
                }
                var0?.EndRestartableGroup(2004);
            }


            Add1AndCallIfEven(someVal, ((DotNetCompose.Runtime.ComposableAction)(() =>
            {
                Inner(null);
            })));

            Add1AndCallIfOdd(someVal, ((DotNetCompose.Runtime.ComposableAction)(() =>
            {
                Inner2(null);
            })));
            var0?.EndGroup(2001);
        }


        [DotNetCompose.Runtime.ComposeGeneratedAttribute, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void Add1AndCallIfEven_Generated(int i, DotNetCompose.Runtime.ComposableLambdaWrapper action)
        {
            var var0 = DotNetCompose.Runtime.ComposeScope.GetCurrentContext();
            var0?.StartGroup(2002);
            if ((i + 1) % 2 == 0)
            {
                var0?.StartRestartableGroup(2004);
                action.Invoke(2003);
                var0?.EndRestartableGroup(2004);
            }
            var0?.EndGroup(2002);
        }


        [DotNetCompose.Runtime.ComposeGeneratedAttribute, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void Add1AndCallIfOdd_Generated(int i, DotNetCompose.Runtime.ComposableLambdaWrapper action)
        {
            var var0 = DotNetCompose.Runtime.ComposeScope.GetCurrentContext();
            var0?.StartGroup(2003);
            if ((i + 1) % 2 != 0)
            {
                var0?.StartRestartableGroup(2005);
                action.Invoke(2004);
                var0?.EndRestartableGroup(2005);
            }
            var0?.EndGroup(2003);
        }



        [DotNetCompose.Runtime.ComposeGeneratedAttribute, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void Inner_Generated(DotNetCompose.Runtime.ComposableLambdaWrapper action)
        {
            var var0 = DotNetCompose.Runtime.ComposeScope.GetCurrentContext();
            var0?.StartGroup(2004);
            Debug.WriteLine("Inner");
            action.Invoke(2005);
            var0?.EndGroup(2004);
        }


        [DotNetCompose.Runtime.ComposeGeneratedAttribute, System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public static void Inner2_Generated(DotNetCompose.Runtime.ComposableLambdaWrapper action)
        {
            var var0 = DotNetCompose.Runtime.ComposeScope.GetCurrentContext();
            var0?.StartGroup(2005);
            Debug.WriteLine("Inner2");
            action.Invoke(2006);
            var0?.EndGroup(2005);
        }

    }
    #endregion
}
}
