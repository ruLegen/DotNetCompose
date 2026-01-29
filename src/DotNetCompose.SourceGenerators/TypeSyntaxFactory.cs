using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    public static class TypeSyntaxFactory
    {
        /// <summary>
        /// Used to generate a type without generic arguments
        /// </summary>
        /// <param name="identifier">The name of the type to be generated</param>
        /// <returns>An instance of TypeSyntax from the Roslyn Model</returns>
        public static TypeSyntax GetTypeSyntax(string identifier)
        {
            return
                SyntaxFactory.IdentifierName(
                    SyntaxFactory.Identifier(identifier)
                );
        }

        // 1. Simple variable
        public static LocalDeclarationStatementSyntax CreateVariable(
            string typeName,
            string variableName,
            ExpressionSyntax initializer = null)
        {
            VariableDeclaratorSyntax declarator = SyntaxFactory.VariableDeclarator(variableName);

            if (initializer != null)
            {
                declarator = declarator.WithInitializer(
                    SyntaxFactory.EqualsValueClause(initializer));
            }

            return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(typeName))
                .WithVariables(SyntaxFactory.SingletonSeparatedList(declarator))
            );
        }
    }
}
