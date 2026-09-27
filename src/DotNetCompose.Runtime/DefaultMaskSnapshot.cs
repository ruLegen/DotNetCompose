using System;

namespace DotNetCompose.Runtime
{
    // An immutable copy of the compiler's transient omission mask. Small masks live
    // entirely in the boxed value stored by a comparison slot or in a cache field.
    internal readonly struct DefaultMaskSnapshot
    {
        private readonly ulong _bits;
        private readonly byte[]? _overflow;

        private DefaultMaskSnapshot(int count, ulong bits, byte[]? overflow)
        {
            Count = count;
            _bits = bits;
            _overflow = overflow;
        }

        internal int Count { get; }

        internal static DefaultMaskSnapshot Capture(ComposableArgumentsDefaultState mask, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count <= 64)
            {
                ulong bits = 0;
                for (int index = 0; index < count; index++)
                    if (mask[index] == ComposableArgumentsDefaultState.ShouldUseDefault)
                        bits |= 1UL << index;
                return new DefaultMaskSnapshot(count, bits, null);
            }

            byte[] overflow = new byte[(count + 7) / 8];
            for (int index = 0; index < count; index++)
                if (mask[index] == ComposableArgumentsDefaultState.ShouldUseDefault)
                    overflow[index / 8] |= (byte)(1 << (index % 8));
            return new DefaultMaskSnapshot(count, 0, overflow);
        }

        internal bool Matches(ComposableArgumentsDefaultState mask, int count)
        {
            if (Count != count)
                return false;
            for (int index = 0; index < count; index++)
            {
                bool stored = count <= 64
                    ? (_bits & (1UL << index)) != 0
                    : (_overflow![index / 8] & (1 << (index % 8))) != 0;
                if (stored != (mask[index] == ComposableArgumentsDefaultState.ShouldUseDefault))
                    return false;
            }
            return true;
        }
    }
}
