using System;

namespace DotNetCompose.Runtime
{
    [AttributeUsage(AttributeTargets.Method,AllowMultiple = false)]
    public class ComposableIgnoreAttribute : Attribute
    {
    }
}