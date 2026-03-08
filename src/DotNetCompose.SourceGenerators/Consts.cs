using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    public static class Consts
    {
        //public const string ComposableActionFullTypeName = "DotNetCompose.Runtime.ComposableAction";
        public const string ComposeGeneratedAttributeFullTypeName = "DotNetCompose.Runtime.ComposeGeneratedAttribute";
        public const string ComposableActionParameterFullTypeName = "DotNetCompose.Runtime.ComposableActionParameterAttribute";


        public const string ComposableAttributeFullName = "DotNetCompose.Runtime.ComposableAttribute";


        public static string NameWithWhiteSpace(string s) => string.Format("{0} ", s);
        public static class ComposeScope
        {
            public const string FullName = "DotNetCompose.Runtime.ComposeScope";
            public const string GetCurrentContextMethodName = "GetCurrentContext";
        }

        public static class ComposeContext
        {
            public const string FullName  = "DotNetCompose.Runtime.IComposeContext";
            public const string StartRestartableGroupMethod  = "StartRestartableGroup";
            public const string EndRestartableGroupMethod  = "EndRestartableGroup";
            public const string StartGroupMethod  = "StartGroup";
            public const string EndGroupMethod  = "EndGroup";
        }
        public static class ComposableLabmdaWrapper
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableLambdaWrapper";
            public const string InvokeMethod = "Invoke";
        }

        public static class ComposableAction
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableAction";
            public const string InvokeMethod = "Invoke";

            public static string FullNameWithGenericArguments(IEnumerable<string> genericNames)
            {
                if(genericNames == null || !genericNames.Any())
                    return FullName;
                return string.Format("{0}<{1}>", FullName, string.Join(",", genericNames));
            }
        }
    }
}
