using Microsoft.CodeAnalysis;
using System;

namespace DotNetCompose.SourceGenerators.Diagnostics
{
    internal interface IDiagnosticReporter
    {
        void Report(DiagnosticInfo diagnostic);

        bool HasErrors { get; }
    }

    internal sealed class NullDiagnosticReporter : IDiagnosticReporter
    {
        public static readonly NullDiagnosticReporter Instance = new();

        private NullDiagnosticReporter() { }

        public bool HasErrors => false;

        public void Report(DiagnosticInfo diagnostic) { }
    }
}
