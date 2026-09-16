using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public static class ComposeHelpers
    {
        public static ComposableLambdaWrapper GetLambda(IComposerContext ctx, int key, Func<Delegate> factory)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            ctx.StartGroup(key);
            try
            {
                object? previous = ctx.RememberedValue();
                Delegate action = factory();
                if (previous is ComposableLambdaWrapper existing && existing.Action.Equals(action)) return existing;
                // A new wrapper keeps captured arguments private to this pending execution.
                ComposableLambdaWrapper wrapper = new ComposableLambdaWrapper(action);
                ctx.UpdateRememberedValue(wrapper);
                return wrapper;
            }
            finally { ctx.EndGroup(); }
        }
    }
}
