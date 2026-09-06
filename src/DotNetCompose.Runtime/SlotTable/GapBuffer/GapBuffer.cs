using System;
using System.Collections.Generic;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    /// <summary>
    /// Modified version of https://github.com/kcartlidge/GapBuffer/blob/main/GapBuffer/GapBuffer.cs
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class GapBuffer<T>
    {
        private const int DefaultCapacity = 10;

        private T[] _buffer;
        private int _gapEndPos;
        private int _gapStartPos;

        public Action<int, int, int>? OnElementsMoved { get; set; }

        public GapBuffer()
        {
            _buffer = new T[DefaultCapacity];
            _gapEndPos = _buffer.Length;
        }

        public int Capacity => _buffer.Length;

        public int Count => _buffer.Length - GapSize;

        private int GapSize => _gapEndPos - _gapStartPos;

        public int Length => Count;

        public T this[int virtualIndex]
        {
            get
            {
                BoundsCheck(virtualIndex);
                return virtualIndex >= _gapStartPos ? _buffer[virtualIndex + GapSize] : _buffer[virtualIndex];
            }
            set
            {
                BoundsCheck(virtualIndex);
                if (virtualIndex >= _gapStartPos) _buffer[virtualIndex + GapSize] = value;
                else _buffer[virtualIndex] = value;
            }
        }

        public T GetAtPhysical(int physicalIndex) => _buffer[physicalIndex];

        public void SetAtPhysical(int physicalIndex, T item) => _buffer[physicalIndex] = item;

        public int Insert(int index, T item)
        {
            if (index < 0 || index > Count) return -1;

            MoveGap(index);
            ResizeGap(1);

            _buffer[index] = item;
            _gapStartPos++;
            return index;
        }

        public void RemoveAtPhysical(int physicalIndex)
        {
            if (physicalIndex < 0 || physicalIndex >= _buffer.Length) return;

            int logicalIndex = physicalIndex < _gapStartPos
                ? physicalIndex
                : physicalIndex - GapSize;

            if (logicalIndex < 0 || logicalIndex >= Count) return;

            MoveGap(logicalIndex);
            _buffer[_gapEndPos] = default!;
            _gapEndPos++;
        }

        public void Add(T item) => Insert(Count, item);

        public void AddRange(IEnumerable<T> items) => InsertRange(Count, items);

        public void InsertRange(int index, IEnumerable<T> items)
        {
            if (index < 0 || index > Count) return;

            var collection = items as ICollection<T> ?? new List<T>(items);
            int insertCount = collection.Count;
            if (insertCount == 0) return;

            MoveGap(index);
            ResizeGap(insertCount);

            if (collection is List<T> list)
            {
                list.CopyTo(_buffer, index);
            }
            else if (collection is T[] array)
            {
                Array.Copy(array, 0, _buffer, index, insertCount);
            }
            else
            {
                int i = index;
                foreach (var item in collection)
                    _buffer[i++] = item;
            }

            _gapStartPos += insertCount;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count) return;

            MoveGap(index);
            _buffer[_gapEndPos] = default!;
            _gapEndPos++;
        }

        public void RemoveRange(int index, int length)
        {
            if (length < 1) return;
            var idx = index + length - 1;
            for (var i = 0; i < length; i++) RemoveAt(idx--);
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _gapStartPos = 0;
            _gapEndPos = _buffer.Length;
        }

        public void SetCapacity(int requestedCapacity)
        {
            if (requestedCapacity == _buffer.Length) return;
            if (requestedCapacity < Count) throw new BufferCapacityException(requestedCapacity, Count);
            if (requestedCapacity > 0)
            {
                var newBuffer = new T[requestedCapacity];
                var newGapEnd = newBuffer.Length - (_buffer.Length - _gapEndPos);

                Array.Copy(_buffer, 0, newBuffer, 0, _gapStartPos);
                Array.Copy(_buffer, _gapEndPos, newBuffer, newGapEnd, newBuffer.Length - newGapEnd);

                var afterGapCount = _buffer.Length - _gapEndPos;
                if (afterGapCount > 0)
                    OnElementsMoved?.Invoke(_gapEndPos, newGapEnd, afterGapCount);

                _buffer = newBuffer;
                _gapEndPos = newGapEnd;
            }
            else
            {
                _buffer = new T[DefaultCapacity];
                _gapStartPos = 0;
                _gapEndPos = _buffer.Length;
            }
        }

        public int IndexOf(T item)
        {
            var foundAt = Array.IndexOf(_buffer, item, 0, _gapStartPos);
            if (foundAt > -1) return foundAt;

            foundAt = Array.IndexOf(_buffer, item, _gapEndPos, _buffer.Length - _gapEndPos);
            if (foundAt > -1) return foundAt - GapSize;

            return -1;
        }

        private void MoveGap(int index)
        {
            if (index == _gapStartPos) return;
            if (GapSize == 0)
            {
                _gapStartPos = _gapEndPos = index;
                return;
            }

            if (index < _gapStartPos)
            {
                var offset = _gapStartPos - index;
                var sizeDiff = GapSize < offset ? GapSize : offset;
                Array.Copy(_buffer, index, _buffer, _gapEndPos - offset, offset);
                OnElementsMoved?.Invoke(index, _gapEndPos - offset, offset);
                _gapStartPos -= offset;
                _gapEndPos -= offset;
                Array.Clear(_buffer, index, sizeDiff);
            }
            else
            {
                var count = index - _gapStartPos;
                var deltaIndex = index > _gapEndPos ? index : _gapEndPos;
                Array.Copy(_buffer, _gapEndPos, _buffer, _gapStartPos, count);
                OnElementsMoved?.Invoke(_gapEndPos, _gapStartPos, count);
                _gapStartPos += count;
                _gapEndPos += count;
                Array.Clear(_buffer, deltaIndex, _gapEndPos - deltaIndex);
            }
        }

        private void ResizeGap(int requiredGapSize)
        {
            if (requiredGapSize <= GapSize) return;

            var newCapacity = (Count + requiredGapSize) * 2;
            if (newCapacity < DefaultCapacity) newCapacity = DefaultCapacity;
            SetCapacity(newCapacity);
        }

        private void BoundsCheck(int index)
        {
            if (index < 0 || index >= Count) throw new BufferAccessException(index, Count);
        }
    }
}
