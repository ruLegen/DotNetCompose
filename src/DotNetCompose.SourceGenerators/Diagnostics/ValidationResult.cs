using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Diagnostics
{
    internal abstract record ValidationResult(DiagnosticInfo? Diagnostic);

    internal sealed record ClassResult(
        ClassAndComposablesMethods Class,
        DiagnosticInfo? Diagnostic) : ValidationResult(Diagnostic)
    {
        public bool IsValid => Diagnostic == null;
    }

    internal sealed record MethodResult(
        MethodFullNameAndDeclaration Method,
        DiagnosticInfo? Diagnostic) : ValidationResult(Diagnostic)
    {
        public bool IsValid => Diagnostic == null;
    }
}
