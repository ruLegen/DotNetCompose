using System;

namespace DotNetCompose.Runtime
{
    public interface IDefaultValueProvider
    {
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public class DefaultAttribute<T> : Attribute where T : IDefaultValueProvider
    {
        public Type DefaultValueProviderType { get; }
        public DefaultAttribute()
        {
            DefaultValueProviderType = typeof(T);
        }
    }
}
