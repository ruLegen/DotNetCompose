using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    public static class Consts
    {
        public const string ComposableActionFullTypeName = "DotNetCompose.Runtime.ComposableAction";
        public const string ComposableLambdaFullTypeName = "DotNetCompose.Runtime.ComposableLambdaWrapper";

        public static string NameWithWhiteSpace(string s) => string.Format("{0} ", s);
    }
}
