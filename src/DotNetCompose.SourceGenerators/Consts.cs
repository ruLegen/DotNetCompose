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
        public const string ComposableIgnoreAttributeFullName = "DotNetCompose.Runtime.ComposableIgnoreAttribute";
        public const string DefaultAttributeFullName = "DotNetCompose.Runtime.DefaultAttribute`1";

        public const string DefaultEOL = "\r\n";
        public static string DefaultIndent = new string(' ',4);

        public static class Rewriter
        {
            public const string ContextParamName = "__ctx";
            public const string ChangedParamName = "__changed";
            public const string DefaultParamName = "__defaultParamState";
            public const string StoredLambdaClassName = "__StoredLambda";
            public const string BuildersClassName = "Builders";
        }

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

            public const string StartReplaceableGroupMethod  = "StartReplaceableGroup";
            public const string EndReplaceableGroupMethod  = "EndReplaceableGroup";

            public const string StartMovableGroupMethod  = "StartRestartableGroup";
            public const string EndMovableGroupMethod  = "StartRestartableGroup";

            public const string ChangedMethod = "Changed";
            public const string SkippingProperty = "Skipping";
            public const string SkipToGroupEndMethod = "SkipToGroupEnd";
        }
        public static class ComposableArgumentsState
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableArgumentsState";
            public const string SameField = "Same";
            public const string DifferentField = "Different";
            public const string UncertainField = "Uncertain";
            public const string StaticField = "Static";
            public const string ForceField = "Force";
        }
        public static class ComposeUpdateScope
        {
            public const string FullName = "DotNetCompose.Runtime.IComposeUpdateScope";
            public const string UpdateScopeMethod = "UpdateScope";
        }
        public static class ComposableArgumentsDefaultState
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableArgumentsDefaultState";
            public const string DefaultParamName = "_defaultParamState";
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
