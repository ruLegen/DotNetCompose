using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace DotNetCompose.SourceGenerators.Diagnostics
{
    internal sealed class DiagnosticReporter : IDiagnosticReporter
    {
        private readonly ImmutableArray<DiagnosticInfo>.Builder _builder = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        public bool HasErrors { get; private set; }

        public void Report(DiagnosticInfo diagnostic)
        {
            if (diagnostic.Descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                HasErrors = true;
            _builder.Add(diagnostic);
        }

        public ImmutableArray<DiagnosticInfo> ToImmutable() => _builder.ToImmutable();
    }
}
