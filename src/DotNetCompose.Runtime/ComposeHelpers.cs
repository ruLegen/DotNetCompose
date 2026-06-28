using System;
using System.Collections.Generic;
using System.Text;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime
{
    public static class ComposeHelpers
    {
        private static Dictionary<IComposerContext, Dictionary<int, ComposableLambdaWrapper>> _cache 
            = new Dictionary<IComposerContext, Dictionary<int, ComposableLambdaWrapper>>();
        public static ComposableLambdaWrapper GetLambda(IComposerContext ctx, int key, Func<Delegate> factory)
        {
            if (!_cache.TryGetValue(ctx, out var labmdaWrapperCaches))
            {
                labmdaWrapperCaches = new Dictionary<int, ComposableLambdaWrapper>();
                _cache[ctx] = labmdaWrapperCaches;
            }
            
            if(!labmdaWrapperCaches.TryGetValue(key, out var wrapper))
            {
                wrapper = new ComposableLambdaWrapper(factory.Invoke());
                labmdaWrapperCaches[key] = wrapper;
            }
            return wrapper;
        }
    }
}
