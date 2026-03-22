using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Text;
#nullable enable
namespace DotNetCompose.SourceGenerators.Extensions
{
    internal static class IndentedTextWriterExtensions
    {
        public static void AppendLine(this IndentedTextWriter writer, string text) 
        {
            if(text != null)
                writer.Write(text);
            writer.WriteLine();
        }

        public static void AppendLineRaw(this IndentedTextWriter writer, string text)
        {
            int lastIndent = writer.Indent;
            writer.Indent = 0;
            if (text != null)
                writer.Write(text);
            writer.WriteLine();
            writer.Indent = lastIndent; 
        }
        public static void AppendLine(this IndentedTextWriter writer)
        {
            writer.WriteLine();
        }

        public static void WithIndent(this IndentedTextWriter writer, Action action)
        {
            writer.Indent++;
            action();
            writer.Indent--;
        }
    }
}
