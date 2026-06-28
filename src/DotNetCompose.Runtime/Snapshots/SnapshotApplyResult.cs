namespace DotNetCompose.Runtime.Snapshots
{
    public abstract class SnapshotApplyResult
    {
        public static readonly SnapshotApplyResult Success = new SuccessResult();
        public static SnapshotApplyResult Failure(string message) => new FailureResult(message);

        public abstract bool Succeeded { get; }

        private SnapshotApplyResult() { }

        public sealed class SuccessResult : SnapshotApplyResult
        {
            public override bool Succeeded => true;
        }

        public sealed class FailureResult : SnapshotApplyResult
        {
            public string Message { get; }
            internal FailureResult(string message) { Message = message; }
            public override bool Succeeded => false;
        }
    }
}
