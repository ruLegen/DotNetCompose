using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Snapshots
{
    public static class SnapshotId
    {
        public const long Invalid = 0;
        public const long Initial = 1;
        private const long AlignmentSize = 128L;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long AlignToLowerBound(long id)
        {
            var aligned = (id / AlignmentSize) * AlignmentSize;
            return aligned < 0 ? long.MaxValue - AlignmentSize + 1 : aligned;
        }
    }
}
