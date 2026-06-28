using System;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.CompositionLocal
{
    /// <summary>
    /// Provides values for CompositionLocal keys to the composable subtree.
    /// </summary>
    public static class CompositionLocalProviderExtensions
    {
        /// <summary>
        /// Binds values to ProvidableCompositionLocal keys for all composable functions
        /// called directly or indirectly in the content lambda.
        /// </summary>
        /// <param name="values">The values to provide.</param>
        /// <param name="content">The composable content that receives the provided values.</param>
        public static void CompositionLocalProvider(
            ProvidedValue[] values,
            Action content)
        {
            var ctx = ComposeScope.GetCurrentContext();
            if (ctx == null)
                throw new InvalidOperationException(
                    "CompositionLocalProvider must be called within a composable context");

            ctx.StartProviders(values);
            try
            {
                content();
            }
            finally
            {
                ctx.EndProviders();
            }
        }

        /// <summary>
        /// Binds a single value to a ProvidableCompositionLocal key for the content lambda.
        /// </summary>
        public static void CompositionLocalProvider(
            ProvidedValue value,
            Action content)
        {
            CompositionLocalProvider(new[] { value }, content);
        }
    }
}
