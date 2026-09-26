using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;

namespace DotNetCompose.SourceGenerators.Diagnostics
{
    internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

        public static LocationInfo? FromLocation(Location? location)
        {
            if (location?.SourceTree is null)
                return null;
            return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
        }
    }

    internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, object?[]? MessageArgs)
    {
        public DiagnosticInfo(DiagnosticDescriptor descriptor, LocationInfo? location)
            : this(descriptor, location, null) { }

        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params object[]? MessageArgs)
             => new DiagnosticInfo(descriptor, LocationInfo.FromLocation(location), MessageArgs);
        public Diagnostic ToDiagnostic() => Diagnostic.Create(Descriptor, Location?.ToLocation(), MessageArgs);
    }

    internal static class DiagnosticDescriptors
    {
        private const string Category = "ComposeGenerator";

        public static readonly DiagnosticDescriptor DNC001_ExpressionBodiedNotSupported = new DiagnosticDescriptor(
            id: "DNC001",
            title: "Expression-bodied method not supported",
            messageFormat: "Method '{0}' uses an expression body, which is not supported for [Composable] methods",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC002_IfWithoutBlock = new DiagnosticDescriptor(
            id: "DNC002",
            title: "If statement without block body",
            messageFormat: "If statement without a block body is not supported inside [Composable] methods",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC003_ForWithoutBlock = new DiagnosticDescriptor(
            id: "DNC003",
            title: "For statement without block body",
            messageFormat: "For statement without a block body is not supported inside [Composable] methods",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC004_ForeachWithoutBlock = new DiagnosticDescriptor(
            id: "DNC004",
            title: "Foreach statement without block body",
            messageFormat: "Foreach statement without a block body is not supported inside [Composable] methods",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC005_DirectComposableReference = new DiagnosticDescriptor(
            id: "DNC005",
            title: "Direct composable method reference",
            messageFormat: "Direct reference to composable method '{0}' is not supported; use a lambda expression instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC006_UnrecognizedLambda = new DiagnosticDescriptor(
            id: "DNC006",
            title: "Unrecognized lambda expression",
            messageFormat: "Lambda expression type not recognized in composable call; only simple and parenthesized lambdas are supported",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC007_LambdaBodyConversion = new DiagnosticDescriptor(
            id: "DNC007",
            title: "Lambda body conversion failed",
            messageFormat: "Failed to convert lambda body in composable call; expected a block expression or arrow expression",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC008_MemberAccessNotFound = new DiagnosticDescriptor(
            id: "DNC008",
            title: "Member access resolution failed",
            messageFormat: "Unable to resolve member access expression in composable method call",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC009_ConditionalAccessFailed = new DiagnosticDescriptor(
            id: "DNC009",
            title: "Conditional access resolution failed",
            messageFormat: "Failed to resolve conditional access invocation in delegate call",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC010_ClassNotPartial = new DiagnosticDescriptor(
            id: "DNC010",
            title: "Class must be partial",
            messageFormat: "Class '{0}' must be declared as 'partial' because it contains [Composable] methods",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC012_AsyncComposable = new DiagnosticDescriptor(
            id: "DNC012",
            title: "Async composable method is unsupported",
            messageFormat: "Composable method '{0}' cannot be async; launch asynchronous work with LaunchedEffect",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC013_IteratorComposable = new DiagnosticDescriptor(
            id: "DNC013",
            title: "Iterator composable method is unsupported",
            messageFormat: "Composable method '{0}' cannot contain yield statements",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC014_ByRefComposableParameter = new DiagnosticDescriptor(
            id: "DNC014",
            title: "By-reference composable parameter is unsupported",
            messageFormat: "Composable method '{0}' cannot declare ref, out, or in parameters",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC015_DirectComposableCall = new DiagnosticDescriptor(
            id: "DNC015",
            title: "Composable called outside composition",
            messageFormat: "Composable method '{0}' cannot be called from non-composable code; use its generated Builders entry point as composition content",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC016_NonReadOnlyCall = new DiagnosticDescriptor(
            id: "DNC016",
            title: "Non-read-only composable call",
            messageFormat: "Read-only composable code cannot call non-read-only composable '{0}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC017_UnverifiableReadOnlyDelegate = new DiagnosticDescriptor(
            id: "DNC017",
            title: "Unverifiable read-only composable delegate",
            messageFormat: "Argument for read-only composable parameter '{0}' must be an inline lambda or a read-only composable parameter",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC018_GeneratedSignatureConflict = new DiagnosticDescriptor(
            id: "DNC018",
            title: "Generated composable signature conflict",
            messageFormat: "Composable method '{0}' conflicts with an existing overload that uses the generated composer signature",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC020_InvalidComposableMode = new DiagnosticDescriptor(
            id: "DNC020",
            title: "Invalid composable mode",
            messageFormat: "Composable mode '{0}' is not valid for {1} '{2}'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC021_InlineComposableEscape = new DiagnosticDescriptor(
            id: "DNC021",
            title: "Inline composable parameter escapes",
            messageFormat: "Inline composable parameter '{0}' can only be invoked or forwarded to another inline composable parameter",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC022_InvalidInlineComposableMethod = new DiagnosticDescriptor(
            id: "DNC022",
            title: "Invalid inline composable method",
            messageFormat: "Inline composable method '{0}' must be concrete and non-virtual",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC023_ComposableOverrideModeMismatch = new DiagnosticDescriptor(
            id: "DNC023",
            title: "Composable override mode mismatch",
            messageFormat: "Composable override '{0}' must preserve the method and parameter modes declared by its base method",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DNC900_InternalError = new DiagnosticDescriptor(
            id: "DNC900",
            title: "Internal generator error",
            messageFormat: "Internal error: {0}",
            category: Category + ".Internal",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
