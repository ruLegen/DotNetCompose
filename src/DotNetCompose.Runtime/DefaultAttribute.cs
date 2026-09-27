using System;

namespace DotNetCompose.Runtime
{
    /// <summary>
    /// Marker for a default provider with an accessible static parameterless Create method.
    /// The source generator validates the method because the runtime also targets netstandard2.1.
    /// </summary>
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
