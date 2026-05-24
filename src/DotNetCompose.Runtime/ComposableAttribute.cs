using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
    public class ComposableAttribute : Attribute
    {
    }
}
