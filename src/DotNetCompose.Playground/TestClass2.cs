using DotNetCompose.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCompose.Playground
{
    internal static partial class TestClass2
    {
        public class Buildes
        {
            public static void InBuilder() { }
        }

        [Composable]
        public static void ComeComposableInAnotherClass()
        {
    
        }
    }
}
