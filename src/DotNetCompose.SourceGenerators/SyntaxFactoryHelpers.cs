using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.SourceGenerators
{
    public static class SyntaxFactoryHelpers
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

        public static LocalDeclarationStatementSyntax CreateStaticCallWithVar(
            string className,
            string methodName,
            string variableName,
            params ExpressionSyntax[] arguments)
        {
            return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var").WithTrailingTrivia(SyntaxFactory.Space))
                .WithVariables(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(variableName).WithTrailingTrivia(SyntaxFactory.Space)
                        .WithInitializer(
                            SyntaxFactory.EqualsValueClause(
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(
                                        SyntaxKind.SimpleMemberAccessExpression,
                                        SyntaxFactory.IdentifierName(className),
                                        SyntaxFactory.IdentifierName(methodName)
                                    )
                                )
                                .WithArgumentList(CreateArgumentList(arguments))
                            )
                        )
                    )
                )
            ).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        public static AttributeSyntax CreateEditorNotVisibleAttribute()
        {
            return SyntaxFactory.Attribute(
                      SyntaxFactory.IdentifierName("System.ComponentModel.EditorBrowsable"),
                      SyntaxFactory.AttributeArgumentList(
                          SyntaxFactory.SeparatedList<AttributeArgumentSyntax>(
                              new SyntaxNodeOrToken[]
                              {
                                  SyntaxFactory.AttributeArgument(
                                      SyntaxFactory.MemberAccessExpression(
                                          SyntaxKind.SimpleMemberAccessExpression,
                                          SyntaxFactory.IdentifierName("System.ComponentModel.EditorBrowsableState"),
                                          SyntaxFactory.IdentifierName("Never")
                                     ))
                              }
                       )));
        }

        public static ExpressionStatementSyntax CreateMethodCallOnVariableWithArgs(
            string variableName,
            string methodName,
            params ExpressionSyntax[] arguments)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(variableName),
                        SyntaxFactory.IdentifierName(methodName)
                    )
                )
                .WithArgumentList(CreateArgumentList(arguments))
            ).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }
        public static ExpressionStatementSyntax CreateSafeMethodCallOnVariableWithArgs(
           string variableName,
           string methodName,
           params ExpressionSyntax[] arguments)
        {
            return SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.ConditionalAccessExpression(
                        SyntaxFactory.IdentifierName(variableName),
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberBindingExpression(SyntaxFactory.IdentifierName(methodName))
                        )
                .WithArgumentList(CreateArgumentList(arguments))
            )
            ).WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }
        public static ExpressionSyntax CreateIntLiteral(int value)
        {
            return SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(value)
            );
        }
        private static ArgumentListSyntax CreateArgumentList(params ExpressionSyntax[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                return SyntaxFactory.ArgumentList();

            var args = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0) args.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
                args.Add(SyntaxFactory.Argument(arguments[i]));
            }

            return SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList<ArgumentSyntax>(args));
        }

    }
}
