using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public static string DefaultIndent = new string(' ', 4);

        public static class Rewriter
        {
            public const string ContextParamName = "__ctx";
            public const string ChangedParamName = "__changed";
            public const string DefaultParamName = "__defaultParamState";
            public const string StoredLambdaClassName = "__StoredLambda";
            public const string BuildersClassName = "Builders";
            public const string LambdaValueName = "a";
            public static readonly Func<int, int, string> LambdaName = (methodIndex, lambdaIndex) =>
                $"__Lambda_{(uint)methodIndex}_{(uint)lambdaIndex}";
            public static readonly Func<string, string> ParameterStateName = name => $"__{name}_state";
        }

        public static class Restart
        {
            public const string ScopeUpdaterName = "__dncScopeUpdater";
            public const string ContextName = "__dncRestartContext";
            public static readonly Func<int, string> ChangedName = index => $"__dncRestartChanged{index}";
            public static readonly Func<int, string> DefaultName = index => $"__dncRestartDefault{index}";
        }

        public static class Defaults
        {
            public const string CacheName = "__dncDefaultsCache";
            public const string MaskMatchesName = "__dncDefaultMaskMatches";
            public const string MaskChangedName = "__dncDefaultMaskChanged";
        }

        public static class DefaultProvider
        {
            public const string CreateMethod = "Create";
        }

        public static class ComposableDefaultsCache
        {
            public const string FullName = "global::DotNetCompose.Runtime.ComposableDefaultsCache";
            public const string MatchesMethod = "Matches";
            public const string GetMethod = "Get";
        }

        public static class ComposeHelpers
        {
            public const string Name = "ComposeHelpers";
            public const string GetLambdaMethod = "GetLambda";
            public const string GetReadonlyLambdaMethod = "GetReadonlyLambda";
        }

        public static class MovableContent
        {
            public const string InvokeMethod = ComposableAction.InvokeMethod;
            public const string InsertMethod = "InsertMovableContent";
        }

        public static class EditorBrowsable
        {
            public const string NeverField = "Never";
        }

        public static string NameWithWhiteSpace(string s) => string.Format("{0} ", s);
        public static class ComposeContext
        {
            public const string FullName = "DotNetCompose.Runtime.Composer.IComposerContext";
            public const string StartRestartableGroupMethod = "StartRestartableGroup";
            public const string EndRestartableGroupMethod = "EndRestartableGroup";

            public const string StartReplaceableGroupMethod = "StartReplaceableGroup";
            public const string EndReplaceableGroupMethod = "EndReplaceableGroup";

            public const string StartMovableGroupMethod = "StartMovableGroup";
            public const string EndMovableGroupMethod = "EndMovableGroup";

            public const string ChangedMethod = "Changed";
            public const string ChangedDefaultMaskMethod = "ChangedDefaultMask";
            public const string ResolveDefaultParameterStateMethod = "ResolveDefaultParameterState";
            public const string RememberedValueMethod = "RememberedValue";
            public const string UpdateRememberedValueMethod = "UpdateRememberedValue";
            public const string StartDefaultsMethod = "StartDefaults";
            public const string EndDefaultsMethod = "EndDefaults";
            public const string DefaultsInvalidProperty = "DefaultsInvalid";
            public const string SkippingProperty = "Skipping";
            public const string SkipToGroupEndMethod = "SkipToGroupEnd";
        }
        public static class CompositionDiagnostics
        {
            public const string RuntimeFullName = "global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsRuntime";
            public const string TokenFullName = "global::DotNetCompose.Runtime.Diagnostics.CompositionDiagnosticsToken";
            public const string OutcomeFullName = "global::DotNetCompose.Runtime.Diagnostics.ComposableExecutionOutcome";
            public const string BeginMethod = "Begin";
            public const string EndMethod = "End";
            public const string IsSupportedProperty = "IsSupported";
            public const string IsActiveProperty = "IsActive";
            public const string TokenName = "__dncDiagnostics";
            public const string OutcomeName = "__dncOutcome";
            public const string EndLabelName = "__dncDiagnosticsEnd";
            public const string ExecutedField = "Executed";
            public const string SkippedField = "Skipped";
        }
        public static class ComposableArgumentsState
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableArgumentsState";
            public const string SameField = "Same";
            public const string DifferentField = "Different";
            public const string UncertainField = "Uncertain";
            public const string StaticField = "Static";
            public const string ForceField = "Force";
            public const string IsForcedProperty = "IsForced";
            public const string ForcedMethod = "Forced";
            public const string NormalizeForRestartMethod = "NormalizeForRestart";
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
            public const string ShouldUseDefaultField = "ShouldUseDefault";
        }

        public static class ComposableLabmdaWrapper
        {
            public const string FullName = "DotNetCompose.Runtime.ComposableLambdaWrapper";
            public const string InvokeMethod = ComposableAction.InvokeMethod;
        }

        public static class ComposableAction
        {
            public const string Name = "ComposableAction";
            public const string FullName = "DotNetCompose.Runtime.ComposableAction";
            public const string InvokeMethod = "Invoke";

            public static string FullNameWithGenericArguments(IEnumerable<string> genericNames)
            {
                if (genericNames == null || !genericNames.Any())
                    return FullName;
                return string.Format("{0}<{1}>", FullName, string.Join(",", genericNames));
            }
        }

        public static class ToolInfo
        {
            public const string Name = "DotNetCompose";

            public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            public static readonly string GeneratedFileHeader =
                $"//------------------------------------------------------------------------------{DefaultEOL}" +
                $"// <auto-generated>{DefaultEOL}" +
                $"//     This code was generated by {Name} v{Version}.{DefaultEOL}" +
                $"//     DO NOT EDIT.{DefaultEOL}" +
                $"//{DefaultEOL}" +
                $"//     Changes to this file may cause incorrect behavior and will be lost if{DefaultEOL}" +
                $"//     the code is regenerated.{DefaultEOL}" +
                $"// </auto-generated>{DefaultEOL}" +
                $"//------------------------------------------------------------------------------";
        }
    }
}
