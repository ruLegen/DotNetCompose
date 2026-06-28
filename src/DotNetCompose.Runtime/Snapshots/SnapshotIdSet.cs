using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.Snapshots
{
    public class SnapshotIdSet : IEnumerable<long>
    {
        public static readonly SnapshotIdSet Empty = new SnapshotIdSet(0, 0, 0, null);

        private readonly long _upperSet;
        private readonly long _lowerSet;
        private readonly long _lowerBound;
        private readonly long[]? _belowBound;

        private SnapshotIdSet(long upperSet, long lowerSet, long lowerBound, long[]? belowBound)
        {
            _upperSet = upperSet;
            _lowerSet = lowerSet;
            _lowerBound = lowerBound;
            _belowBound = belowBound;
        }

        public bool IsEmpty => _upperSet == 0 && _lowerSet == 0 && (_belowBound == null || _belowBound.Length == 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Get(long id)
        {
            var offset = id - _lowerBound;
            if (offset >= 0 && offset < 64)
                return (_lowerSet & (1L << (int)offset)) != 0;
            if (offset >= 64 && offset < 128)
                return (_upperSet & (1L << ((int)offset - 64))) != 0;
            if (offset > 0)
                return false;
            if (_belowBound != null)
                return Array.BinarySearch(_belowBound, id) >= 0;
            return false;
        }

        public SnapshotIdSet Set(long id)
        {
            var offset = id - _lowerBound;
            if (offset >= 0 && offset < 64)
            {
                var mask = 1L << (int)offset;
                if ((_lowerSet & mask) == 0)
                    return new SnapshotIdSet(_upperSet, _lowerSet | mask, _lowerBound, _belowBound);
            }
            else if (offset >= 64 && offset < 128)
            {
                var mask = 1L << ((int)offset - 64);
                if ((_upperSet & mask) == 0)
                    return new SnapshotIdSet(_upperSet | mask, _lowerSet, _lowerBound, _belowBound);
            }
            else if (offset >= 128)
            {
                if (!Get(id))
                {
                    long newUpperSet = _upperSet, newLowerSet = _lowerSet;
                    long newLowerBound = _lowerBound;
                    long[]? newBelowBound = null;
                    var targetLowerBound = SnapshotId.AlignToLowerBound(id);

                    while (newLowerBound < targetLowerBound)
                    {
                        if (newLowerSet != 0)
                        {
                            newBelowBound = AppendBitsToArray(newBelowBound ?? _belowBound, newLowerSet, newLowerBound);
                        }
                        if (newUpperSet == 0)
                        {
                            newLowerBound = targetLowerBound;
                            newLowerSet = 0;
                            break;
                        }
                        newLowerSet = newUpperSet;
                        newUpperSet = 0;
                        newLowerBound += 64;
                    }

                    return new SnapshotIdSet(newUpperSet, newLowerSet, newLowerBound, newBelowBound).Set(id);
                }
            }
            else
            {
                if (_belowBound == null)
                    return new SnapshotIdSet(_upperSet, _lowerSet, _lowerBound, new[] { id });

                var location = Array.BinarySearch(_belowBound, id);
                if (location < 0)
                {
                    var insertLocation = ~location;
                    var newBelow = InsertIntoArray(_belowBound, insertLocation, id);
                    return new SnapshotIdSet(_upperSet, _lowerSet, _lowerBound, newBelow);
                }
            }

            return this;
        }

        public SnapshotIdSet Clear(long id)
        {
            var offset = id - _lowerBound;
            if (offset >= 0 && offset < 64)
            {
                var mask = 1L << (int)offset;
                if ((_lowerSet & mask) != 0)
                    return new SnapshotIdSet(_upperSet, _lowerSet & ~mask, _lowerBound, _belowBound);
            }
            else if (offset >= 64 && offset < 128)
            {
                var mask = 1L << ((int)offset - 64);
                if ((_upperSet & mask) != 0)
                    return new SnapshotIdSet(_upperSet & ~mask, _lowerSet, _lowerBound, _belowBound);
            }
            else if (offset < 0 && _belowBound != null)
            {
                var location = Array.BinarySearch(_belowBound, id);
                if (location >= 0)
                    return new SnapshotIdSet(_upperSet, _lowerSet, _lowerBound, RemoveFromArray(_belowBound, location));
            }

            return this;
        }

        public SnapshotIdSet AndNot(SnapshotIdSet other)
        {
            if (other == Empty) return this;
            if (this == Empty) return Empty;
            if (other._lowerBound == _lowerBound && ReferenceEquals(other._belowBound, _belowBound))
                return new SnapshotIdSet(_upperSet & ~other._upperSet, _lowerSet & ~other._lowerSet, _lowerBound, _belowBound);
            return FastFold(other._belowBound, other._lowerSet, other._upperSet, other._lowerBound, this, (acc, id) => acc.Clear(id));
        }

        public SnapshotIdSet And(SnapshotIdSet other)
        {
            if (other == Empty || this == Empty) return Empty;
            if (other._lowerBound == _lowerBound && ReferenceEquals(other._belowBound, _belowBound))
            {
                var newUpper = _upperSet & other._upperSet;
                var newLower = _lowerSet & other._lowerSet;
                return newUpper == 0 && newLower == 0 && _belowBound == null
                    ? Empty
                    : new SnapshotIdSet(newUpper, newLower, _lowerBound, _belowBound);
            }
            if (_belowBound == null)
                return FastFold(_belowBound, _lowerSet, _upperSet, _lowerBound, Empty, (acc, id) => other.Get(id) ? acc.Set(id) : acc);
            else
                return FastFold(other._belowBound, other._lowerSet, other._upperSet, other._lowerBound, Empty, (acc, id) => Get(id) ? acc.Set(id) : acc);
        }

        public SnapshotIdSet Or(SnapshotIdSet other)
        {
            if (other == Empty) return this;
            if (this == Empty) return other;
            if (other._lowerBound == _lowerBound && ReferenceEquals(other._belowBound, _belowBound))
                return new SnapshotIdSet(_upperSet | other._upperSet, _lowerSet | other._lowerSet, _lowerBound, _belowBound);
            if (_belowBound == null)
                return FastFold(_belowBound, _lowerSet, _upperSet, _lowerBound, other, (acc, id) => acc.Set(id));
            else
                return FastFold(other._belowBound, other._lowerSet, other._upperSet, other._lowerBound, this, (acc, id) => acc.Set(id));
        }

        public long Lowest(long defaultValue = 0)
        {
            if (_belowBound != null && _belowBound.Length > 0)
                return _belowBound[0];
            if (_lowerSet != 0)
                return _lowerBound + TrailingZeroCount(_lowerSet);
            if (_upperSet != 0)
                return _lowerBound + 64 + TrailingZeroCount(_upperSet);
            return defaultValue;
        }

        public SnapshotIdSet AddRange(long from, long until)
        {
            var result = this;
            for (long i = from; i < until; i++)
                result = result.Set(i);
            return result;
        }

        public IEnumerator<long> GetEnumerator()
        {
            if (_belowBound != null)
                foreach (var id in _belowBound)
                    yield return id;
            if (_lowerSet != 0)
            {
                for (int i = 0; i < 64; i++)
                    if ((_lowerSet & (1L << i)) != 0)
                        yield return _lowerBound + i;
            }
            if (_upperSet != 0)
            {
                for (int i = 0; i < 64; i++)
                    if ((_upperSet & (1L << i)) != 0)
                        yield return _lowerBound + 64 + i;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() =>
            $"SnapshotIdSet([{string.Join(",", this)}])";

        private static SnapshotIdSet FastFold(
            long[]? srcBelow, long srcLower, long srcUpper, long srcBound,
            SnapshotIdSet initial,
            Func<SnapshotIdSet, long, SnapshotIdSet> op)
        {
            var acc = initial;
            if (srcBelow != null)
                foreach (var id in srcBelow)
                    acc = op(acc, id);
            if (srcLower != 0)
                for (int i = 0; i < 64; i++)
                    if ((srcLower & (1L << i)) != 0)
                        acc = op(acc, srcBound + i);
            if (srcUpper != 0)
                for (int i = 0; i < 64; i++)
                    if ((srcUpper & (1L << i)) != 0)
                        acc = op(acc, srcBound + 64 + i);
            return acc;
        }

        private static long[] AppendBitsToArray(long[]? existing, long bits, long baseId)
        {
            var count = PopCount(bits);
            if (count == 0) return existing ?? Array.Empty<long>();

            var newArray = new long[(existing?.Length ?? 0) + count];
            if (existing != null)
                existing.CopyTo(newArray, 0);

            int idx = existing?.Length ?? 0;
            for (int i = 0; i < 64; i++)
                if ((bits & (1L << i)) != 0)
                    newArray[idx++] = baseId + i;

            Array.Sort(newArray);
            return newArray;
        }

        private static long[] InsertIntoArray(long[] array, int index, long value)
        {
            var result = new long[array.Length + 1];
            if (index > 0)
                Array.Copy(array, 0, result, 0, index);
            result[index] = value;
            if (index < array.Length)
                Array.Copy(array, index, result, index + 1, array.Length - index);
            return result;
        }

        private static long[] RemoveFromArray(long[] array, int index)
        {
            var result = new long[array.Length - 1];
            if (index > 0)
                Array.Copy(array, 0, result, 0, index);
            if (index < array.Length - 1)
                Array.Copy(array, index + 1, result, index, array.Length - index - 1);
            return result;
        }

        private static int TrailingZeroCount(long value)
        {
            if (value == 0) return 64;
            int count = 0;
            while ((value & 1) == 0) { count++; value >>= 1; }
            return count;
        }

        private static int PopCount(long value)
        {
            int count = 0;
            while (value != 0) { count += (int)(value & 1); value >>= 1; }
            return count;
        }
    }
}
