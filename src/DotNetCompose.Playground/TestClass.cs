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
            for (int i = 0; i < someVal; i++)
            {
                Add1AndCallIfEven(i, () => { });
                if(i == 0)
                {
                    Add1AndCallIfOdd(i, () => { });
                }else if(i == 1)
                {
                    Add1AndCallIfOdd(i, () => { });
                }
            }

            foreach (var i in Enumerable.Range(0,10))
            {
                Add1AndCallIfEven(i, () => { });
                if(i == 0)
                    Add1AndCallIfOdd(i, () => { });
            }
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
    }
}
