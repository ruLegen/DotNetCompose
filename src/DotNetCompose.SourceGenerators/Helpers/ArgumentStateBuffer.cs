using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCompose.SourceGenerators
{
    internal enum ArgumentStateBufferStorage
    {
        StackAlloc,
        CollectionExpression,
        Array
    }
}

namespace DotNetCompose.SourceGenerators.Helpers
{
    internal static class ArgumentStateBuffer
    {
        public static ArgumentStateBufferStorage SelectStorage(
            Compilation compilation, CSharpParseOptions parseOptions, bool? useStackAlloc)
        {
            // Inspect the consuming compilation, not the build of the generator's DLL.
            if (useStackAlloc ?? compilation.Options.OptimizationLevel == OptimizationLevel.Release)
                return ArgumentStateBufferStorage.StackAlloc;

            return parseOptions.LanguageVersion >= LanguageVersion.CSharp12 &&
                compilation.SupportsRuntimeCapability(RuntimeCapability.InlineArrayTypes)
                    ? ArgumentStateBufferStorage.CollectionExpression
                    : ArgumentStateBufferStorage.Array;
        }

        public static ExpressionSyntax Create(
            IEnumerable<ExpressionSyntax> values, ArgumentStateBufferStorage storage, bool readOnly = false)
        {
            ExpressionSyntax[] elements = values.ToArray();
            if (elements.Length == 0)
                return SyntaxFactory.ParseExpression(readOnly
                    ? "global::System.ReadOnlySpan<byte>.Empty"
                    : "global::System.Span<byte>.Empty");

            if (storage == ArgumentStateBufferStorage.CollectionExpression)
                return SyntaxFactory.CollectionExpression(SyntaxFactory.SeparatedList<CollectionElementSyntax>(
                    elements.Select(value => SyntaxFactory.ExpressionElement(value))));

            ArrayTypeSyntax type = SyntaxFactory.ArrayType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ByteKeyword)),
                SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier()));
            InitializerExpressionSyntax initializer = SyntaxFactory.InitializerExpression(
                SyntaxKind.ArrayInitializerExpression, SyntaxFactory.SeparatedList(elements));
            return storage == ArgumentStateBufferStorage.StackAlloc
                ? SyntaxFactory.StackAllocArrayCreationExpression(type, initializer)
                : SyntaxFactory.ArrayCreationExpression(type, initializer);
        }
    }
}
