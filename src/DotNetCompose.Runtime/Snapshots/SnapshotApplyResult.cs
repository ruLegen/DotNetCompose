using System;

namespace DotNetCompose.Runtime.Snapshots
{
    public readonly record struct SnapshotApplyResult 
    {
        public static SnapshotApplyResult Success => new(true, null);
        public static SnapshotApplyResult Failure(string message) => new(false, message);

        private SnapshotApplyResult(bool succeeded, string? message)
        {
            Succeeded = succeeded;
            Message = message;
        }
        public bool Succeeded { get; }
        public string? Message { get; }
    }
}
