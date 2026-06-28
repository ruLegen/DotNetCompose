using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Composer
{
    public class MovableContent<TParam>
    {
        internal Action<TParam> Content { get; }

        internal MovableContent(Action<TParam> content)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        [Composable, ComposableIgnore]
        public void Invoke(TParam param)
        {
            throw new NotImplementedException("Use composable version");
        }
    }

    public static class MovableContentFactory
    {
        public static MovableContent<TParam> Create<TParam>(Action<TParam> content)
        {
            return new MovableContent<TParam>(content);
        }
    }
}
