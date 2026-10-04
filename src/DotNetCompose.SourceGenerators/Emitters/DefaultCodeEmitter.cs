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
using System.Text.RegularExpressions;
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

            sourceBuilder.AppendLineRaw(ToolInfo.GeneratedFileHeader);
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine("#line hidden");
            sourceBuilder.AppendLine();

            EmitImports(sourceBuilder, input.SourceContext.FileExterns, input.SourceContext.FileUsings);
            sourceBuilder.AppendLine();
            EmitNamespaceScopes(sourceBuilder, input.SourceContext.Namespaces, 0, () =>
            {
                string typeParameters = input.TypeParameters?.WithoutTrivia().ToFullString() ?? string.Empty;
                string constraints = string.Join(" ", input.TypeConstraints.Select(item => item.WithoutTrivia().ToFullString()));
                sourceBuilder.AppendLine($"{input.Accessibility} partial class {input.TypeName}{typeParameters}{(string.IsNullOrEmpty(constraints) ? string.Empty : " " + constraints)}");
                sourceBuilder.AppendLine("{");

                sourceBuilder.WithIndent(() =>
                {
                    int instanceIndent = sourceBuilder.Indent;
                    foreach (SyntaxNode method in input.InstanceMethods)
                    {
                        SyntaxNode mappedMethod = NormalizeAndMap(
                            method,
                            instanceIndent,
                            input.SupportsEnhancedLineDirectives);
                        sourceBuilder.AppendLineRaw(mappedMethod.ToFullString());
                    }

                    if (input.InstanceSessions.Any(session => session.StoredLambdas.Any()))
                        EmitStoredLambdas(sourceBuilder, input.InstanceSessions, input.SupportsEnhancedLineDirectives);

                    if (input.BuilderMethods.Any())
                    {
                        sourceBuilder.AppendLine($"public partial class {Rewriter.BuildersClassName}");
                        sourceBuilder.AppendLine("{");
                        sourceBuilder.WithIndent(() =>
                        {
                            int currentIndent = sourceBuilder.Indent;
                            foreach (SyntaxNode method in input.BuilderMethods)
                            {
                                SyntaxNode mappedMethod = NormalizeAndMap(
                                    method,
                                    currentIndent,
                                    input.SupportsEnhancedLineDirectives);
                                sourceBuilder.AppendLineRaw(mappedMethod.ToFullString());
                            }

                            EmitStoredLambdas(sourceBuilder, input.BuilderSessions, input.SupportsEnhancedLineDirectives);
                        });
                        sourceBuilder.AppendLine("}");
                    }
                });
                sourceBuilder.AppendLine("}");
            });

            return Regex.Replace(
                sourceBuilder.InnerWriter.ToString(),
                @"[ \t]+(?=\r?$)",
                string.Empty,
                RegexOptions.Multiline);
        }

        private static void EmitNamespaceScopes(
            IndentedTextWriter writer,
            ImmutableArray<SourceNamespaceScope> namespaces,
            int index,
            Action emitType)
        {
            if (index == namespaces.Length)
            {
                emitType();
                return;
            }

            SourceNamespaceScope scope = namespaces[index];
            writer.AppendLine($"namespace {scope.Name}");
            writer.AppendLine("{");
            writer.WithIndent(() =>
            {
                EmitImports(writer, scope.Externs, scope.Usings);
                EmitNamespaceScopes(writer, namespaces, index + 1, emitType);
            });
            writer.AppendLine("}");
        }

        private static void EmitImports(
            IndentedTextWriter writer,
            ImmutableArray<ExternAliasDirectiveSyntax> externs,
            ImmutableArray<UsingDirectiveSyntax> usings)
        {
            foreach (ExternAliasDirectiveSyntax directive in externs)
                writer.AppendLine(directive.WithoutTrivia().ToFullString());
            foreach (UsingDirectiveSyntax directive in usings)
                writer.AppendLine(directive.WithoutTrivia().ToFullString());
        }

        private void EmitStoredLambdas(
            IndentedTextWriter sourceBuilder,
            ImmutableArray<RewriterSession> sessions,
            bool supportsEnhancedLineDirectives)
        {
            sourceBuilder.AppendLine($"static partial class {Rewriter.StoredLambdaClassName}");
            sourceBuilder.AppendLine("{");
            sourceBuilder.WithIndent(() =>
            {
                int currentIndent = sourceBuilder.Indent;
                foreach (RewriterSession session in sessions)
                {
                    foreach (var storedLambda in session.StoredLambdas)
                    {
                        SyntaxNode mappedMethod = NormalizeAndMap(
                            storedLambda.MethodDeclaration,
                            currentIndent,
                            supportsEnhancedLineDirectives);
                        sourceBuilder.AppendLineRaw(mappedMethod.ToFullString());
                    }
                }
            });
            sourceBuilder.AppendLine("}");
        }

        private SyntaxNode NormalizeAndMap(
            SyntaxNode method,
            int indentation,
            bool supportsEnhancedLineDirectives)
        {
            SyntaxNode normalizedMethod = SyntaxNormalizer.Normalize(
                method,
                false,
                indentation,
                _indentWhitespace,
                _eolWhitespace);
            return new DebugLineNumberSyntaxTreeWriter(
                supportsEnhancedLineDirectives,
                normalizedMethod).Visit(normalizedMethod)!;
        }
    }
}
