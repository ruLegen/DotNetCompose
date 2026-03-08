using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public static class Composables
    {
        [Composable]
        public static IComposeContext? CurrentContext => throw new NotImplementedException("Internal usage only");

    }
}
