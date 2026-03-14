using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace DotNetCompose.Runtime
{
    public static class Composables
    {
        public class Builders
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static IComposeContext CurrentContext(IComposeContext context, int changed = 0) => context;
            public static T Remember<T>(object key, Func<T> creator, IComposeContext context, int changed = 0)
            {
                return creator.Invoke();
            }
            public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater, IComposeContext context, int changed = 0)
            {

            }
        }

        [Composable]
        public static IComposeContext? CurrentContext() => throw new NotImplementedException("Internal usage only");

        [Composable]
        public static void ComposeNode<T, K>(Func<T> factory, Action<T> updater) => throw new NotImplementedException("Use composable version");

        [Composable]
        public static T Remember<T>(object key, Func<T> creator) => throw new NotImplementedException("Use composable version");
    }
}
