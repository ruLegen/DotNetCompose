using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime
{
    public class DefaultAttribute<T> : Attribute  
    {
        public Type Delegate { get; set; }
        public DefaultAttribute()
        {
            Delegate = typeof(T);
        }
    }
}
