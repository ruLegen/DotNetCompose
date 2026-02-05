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
            if (someVal == 0)
            {
                SomeNonComposableFunction();
            }
            else if (someVal == 1)
            {
                Add1AndCallIfEven(1, null);
            }
            else if (someVal == 2)
            {
                Add1AndCallIfEven(2, null);
            }
            else
            {
                Add1AndCallIfEven(3, null);
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
