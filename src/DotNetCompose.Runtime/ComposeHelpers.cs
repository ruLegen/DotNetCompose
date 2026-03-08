using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public static class ComposeHelpers
    {
        private static Dictionary<IComposeContext, Dictionary<int, ComposableLambdaWrapper>> _cache 
            = new Dictionary<IComposeContext, Dictionary<int, ComposableLambdaWrapper>>();
        public static ComposableLambdaWrapper GetLambda(IComposeContext ctx, int key, Func<Delegate> factory)
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
