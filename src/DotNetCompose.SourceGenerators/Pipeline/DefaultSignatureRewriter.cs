using DotNetCompose.SourceGenerators.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using static DotNetCompose.SourceGenerators.Consts;
using static DotNetCompose.SourceGenerators.Extensions.MethodDeclarationSyntaxExtensions;

namespace DotNetCompose.SourceGenerators.Pipeline
{
    internal sealed class DefaultSignatureRewriter : ISignatureRewriter
    {
        public MethodDeclarationSyntax RewriteSignature(MethodDeclarationSyntax method, TransformationContext context)
        {
            RewriterOptions options = context.Options;
            MethodGenerationContext methodCtx = context.MethodCtx;
            SemanticModel semanticModel = context.SemanticModel;

            bool hasAnyComposables = methodCtx.Parameters.Any(p => p.IsComposable);

            ParameterListSyntax newParameterList = method.ParameterList;
            if (hasAnyComposables)
            {
                newParameterList = ReplaceAllComposableParameters(method, true, semanticModel, methodCtx);
            }
            if (methodCtx.HasDefaultParams)
            {
                newParameterList = newParameterList.WithParameters(
                    SyntaxFactory.SeparatedList(
                        newParameterList.Parameters.Select((p, i) =>
                            i < methodCtx.Parameters.Length && methodCtx.Parameters[i].DefaultProviderType != null
                                ? p.WithDefault(null)
                                : p)));
            }
            newParameterList = AppendComposableContextrelatedParameters(newParameterList, options.ContextVarName, options.ChangedVarName, options.DefaultParamName);

            MethodDeclarationSyntax newMethod = method
                .WithParameterList(newParameterList)
                .WithModifiers(method.Modifiers)
                .WithAttributeLists(ReplaceComposableAttribute(method.AttributeLists));

            return newMethod;
        }

        internal static ParameterListSyntax ReplaceAllComposableParameters(
            MethodDeclarationSyntax method, bool addAttributeToComposableParameters,
            SemanticModel semanticModel, MethodGenerationContext methodCtx)
        {
            var args = method.ParameterList.Parameters.Zip(
                    method.GetParametersInfos(semanticModel),
                    (parameter, paramInfo) => (parameter, paramInfo)
            );

            SeparatedSyntaxList<ParameterSyntax> newArguments = SyntaxFactory.SeparatedList(
                args.Select(s => (Syntax: s.parameter, ParamInfo: s.paramInfo))
                        .Select(oldParam =>
                        SyntaxFactory.Parameter(
                            addAttributeToComposableParameters
                                ? (oldParam.ParamInfo.IsComposable
                                    ? ReplaceComposableActionParameterAttributes(oldParam.Syntax.AttributeLists, semanticModel)
                                    : oldParam.Syntax.AttributeLists)
                                : default,
                            oldParam.Syntax.Modifiers,
                            oldParam.ParamInfo.IsComposable
                                ? SyntaxFactory.ParseTypeName(Consts.ComposableAction.FullNameWithGenericArguments(oldParam.ParamInfo.GenericArguments.Select(t => t.GetFullMetadataName()))).WithTrailingSpace()
                                : oldParam.Syntax.Type,
                            oldParam.Syntax.Identifier,
                            ReplaceDefaultArgumentValue(oldParam.Syntax.Default, oldParam.ParamInfo.IsComposable)
            )));

            return SyntaxFactory.ParameterList(newArguments);
        }

        internal static ParameterListSyntax AppendComposableContextrelatedParameters(
            ParameterListSyntax paramList, string contextParamName, string changedParamName, string defaultParamName)
        {
            SeparatedSyntaxList<ParameterSyntax> newArguments = paramList.Parameters.AddRange(new ParameterSyntax[]
            {
                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(Consts.ComposeContext.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(contextParamName),
                    default),

                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(ComposableArgumentsState.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(changedParamName),
                    default),

                SyntaxFactory.Parameter(default,
                    default,
                    SyntaxFactory.ParseTypeName(Consts.ComposableArgumentsDefaultState.FullName).WithTrailingSpace(),
                    SyntaxFactory.Identifier(defaultParamName),
                    default),
            });

            return paramList.WithParameters(newArguments);
        }

        private static SyntaxList<AttributeListSyntax> ReplaceComposableAttribute(SyntaxList<AttributeListSyntax> attributeLists)
        {
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (IsComposableAttributeSyntax(attribute))
                        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposeGeneratedAttributeFullTypeName));
                    else
                        return attribute;
                });
                return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));
            }));
        }

        private static bool IsComposableAttributeSyntax(AttributeSyntax s)
        {
            var name = s.Name.ToString();
            return name == Consts.ComposableAttributeFullName ||
                    name.EndsWith("Composable") ||
                    name.EndsWith("ComposableAttribute");
        }

        private static SyntaxList<AttributeListSyntax> ReplaceComposableActionParameterAttributes(
            SyntaxList<AttributeListSyntax> attributeLists, SemanticModel semanticModel)
        {
            return SyntaxFactory.List(attributeLists.Select(aList =>
            {
                IEnumerable<AttributeSyntax> newAttributes = aList.Attributes.Select(attribute =>
                {
                    if (MethodDeclarationSyntaxExtensions.IsComposableAttribute(attribute, semanticModel))
                        return SyntaxFactory.Attribute(SyntaxFactory.IdentifierName(Consts.ComposableActionParameterFullTypeName));
                    else
                        return attribute;
                });
                return aList.WithAttributes(SyntaxFactory.SeparatedList(newAttributes));
            }));
        }

        private static EqualsValueClauseSyntax ReplaceDefaultArgumentValue(EqualsValueClauseSyntax defaultSyntax, bool isComposable)
        {
            if (defaultSyntax == null)
                return defaultSyntax;
            if (!isComposable)
                return defaultSyntax;

            return SyntaxFactory.EqualsValueClause(
                            SyntaxFactory.LiteralExpression(
                                SyntaxKind.DefaultLiteralExpression,
                                SyntaxFactory.Token(SyntaxKind.DefaultKeyword)
                            ));
        }
    }
}
