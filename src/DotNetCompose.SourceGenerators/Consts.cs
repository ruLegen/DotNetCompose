using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    public static class Consts
    {
        public const string ComposableActionFullTypeName = "DotNetCompose.Runtime.ComposableAction";
        public const string ComposeGeneratedAttributeFullTypeName = "DotNetCompose.Runtime.ComposeGeneratedAttribute";

        public const string ComposableLambdaFullTypeName = "DotNetCompose.Runtime.ComposableLambdaWrapper";

        public const string ComposableAttributeFullName = "DotNetCompose.Runtime.ComposableAttribute";


        public static string NameWithWhiteSpace(string s) => string.Format("{0} ", s);
        public static class ComposeScope
        {
            public const string FullName = "DotNetCompose.Runtime.ComposeScope";
            public const string GetCurrentContextMethodName = "GetCurrentContext";
        }

        public static class ComposeContext
        {
            public const string StartGroupMethod  = "StartGroup";
            public const string EndGroupMethod  = "EndGroup";
        }
    }
}
