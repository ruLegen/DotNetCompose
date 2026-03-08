using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators.Extensions
{
    public static class SymbolExtensions
    {
        public static bool IsComposableAction(this ISymbol? symbol)
        {
            if (symbol == null) return false;

            //if(symbol is ITypeSymbol typeSymbol) 
            //    return typeSymbol.GetFullMetadataName() == Consts.ComposableActionFullTypeName;
            return false;

        }
        public static string GetFullMetadataName(this ISymbol s)
        {
            if (s is ITypeSymbol symbol && TryGetPrimitiveName(symbol.SpecialType, out string primitiveName))
            {
                return primitiveName;
            }
            if (s == null || IsRootNamespace(s))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(s.MetadataName);
            var last = s;

            s = s.ContainingSymbol;

            while (!IsRootNamespace(s))
            {
                if (s is ITypeSymbol && last is ITypeSymbol)
                {
                    sb.Insert(0, '+');
                }
                else
                {
                    sb.Insert(0, '.');
                }

                sb.Insert(0, s.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                s = s.ContainingSymbol;
            }

            return sb.ToString();
        }

        private static bool TryGetPrimitiveName(SpecialType specialType, out string? name)
        {
            name = specialType switch
            {
                SpecialType.System_Void => "void",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_Char => "char",
                SpecialType.System_SByte => "sbyte",
                SpecialType.System_Byte => "byte",
                SpecialType.System_Int16 => "short",
                SpecialType.System_UInt16 => "ushort",
                SpecialType.System_Int32 => "int",
                SpecialType.System_UInt32 => "uint",
                SpecialType.System_Int64 => "long",
                SpecialType.System_UInt64 => "ulong",
                SpecialType.System_Decimal => "decimal",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                SpecialType.System_String => "string",
                SpecialType.System_IntPtr => "IntPtr",
                SpecialType.System_UIntPtr => "UIntPtr",
                _ => null
            };
            return !string.IsNullOrEmpty(name);
        }

        private static bool IsRootNamespace(ISymbol symbol)
        {
            INamespaceSymbol s = null;
            return ((s = symbol as INamespaceSymbol) != null) && s.IsGlobalNamespace;
        }
    }
}
