using DotNetCompose.SourceGenerators.Extensions;
using DotNetCompose.SourceGenerators.Rewriters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.SyntaxNodeExtensions;

namespace DotNetCompose.SourceGenerators.Emitters
{
    internal sealed class DefaultCodeEmitter : ICodeEmitter
    {
        private readonly string _indentWhitespace;
        private readonly string _eolWhitespace;

        public DefaultCodeEmitter() : this(Consts.DefaultIndent, Consts.DefaultEOL)
        {
        }

        public DefaultCodeEmitter(string indentWhitespace, string eolWhitespace)
        {
            _indentWhitespace = indentWhitespace;
            _eolWhitespace = eolWhitespace;
        }

        public string Emit(CodeGenerationInput input)
        {
            using StringWriter writer = new StringWriter(new StringBuilder(), CultureInfo.InvariantCulture);
            using IndentedTextWriter indentWriter = new IndentedTextWriter(writer, _indentWhitespace);
            IndentedTextWriter sourceBuilder = new IndentedTextWriter(writer);

            foreach (UsingDirectiveSyntax usingDirective in input.Usings)
            {
                sourceBuilder.AppendLine(usingDirective.WithoutTrivia().ToFullString());
            }

            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"namespace {input.Namespace}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.WithIndent(() =>
            {
                sourceBuilder.AppendLine($"{input.Accessibility} partial class {input.TypeName}");
                sourceBuilder.AppendLine("{");

                sourceBuilder.WithIndent(() =>
                {
                    sourceBuilder.AppendLine($"public partial class {Rewriter.BuildersClassName}");
                    sourceBuilder.AppendLine("{");
                    sourceBuilder.WithIndent(() =>
                    {
                        int currentIndent = sourceBuilder.Indent;
                        foreach (SyntaxNode method in input.BuilderMethods)
                        {
                            SyntaxNode normalizedMethod = SyntaxNormalizer.Normalize(method, false, currentIndent, _indentWhitespace, _eolWhitespace);
                            sourceBuilder.AppendLineRaw(normalizedMethod.ToFullString());
                        }

                        sourceBuilder.AppendLine($"static class {Rewriter.StoredLambdaClassName}");
                        sourceBuilder.AppendLine("{");
                        sourceBuilder.WithIndent(() =>
                        {
                            int currentIndent = sourceBuilder.Indent;
                            foreach (RewriterSession session in input.Sessions)
                            {
                                foreach (var storedLambda in session.StoredLambdas)
                                {
                                    var normalizedMethod = SyntaxNormalizer.Normalize(storedLambda.MethodDeclaration, false, currentIndent, _indentWhitespace, _eolWhitespace);
                                    sourceBuilder.AppendLineRaw(normalizedMethod.ToFullString());
                                }
                            }
                        });
                        sourceBuilder.AppendLine("}");
                    });
                    sourceBuilder.AppendLine("}");
                });
                sourceBuilder.AppendLine("}");
            });
            sourceBuilder.AppendLine("}");

            return sourceBuilder.InnerWriter.ToString();
        }
    }
}
