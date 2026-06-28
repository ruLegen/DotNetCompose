using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Snapshots
{
    public static class SnapshotId
    {
        public const long Invalid = 0;
        public const long Initial = 1;
        public const long Preexisting = 1;
        public const long Size = 128L;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long AlignToLowerBound(long id)
        {
            var aligned = (id / Size) * Size;
            return aligned < 0 ? long.MaxValue - Size + 1 : aligned;
        }
    }
}
