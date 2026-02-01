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
        public static void App()
        {
            Inner(() =>
            {
                Inner(null);
            });
        }

        [Composable]
        public static void StartOuter(int i, ComposableAction action)
        {
            Debug.WriteLine("Outer started " + i);
            action();
            action.Invoke();
            action?.Invoke();
        }


        [Composable]
        public static void Inner(ComposableAction action)
        {
            Debug.WriteLine("Inner");
            action.Invoke();
        }

    }
}
