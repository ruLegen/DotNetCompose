using System;

namespace DotNetCompose.Runtime.Snapshots
{
    internal static class ObjectExtensions
    {
        public static T Also<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }

        public static R Let<T, R>(this T obj, Func<T, R> block)
        {
            return block(obj);
        }
    }
}
