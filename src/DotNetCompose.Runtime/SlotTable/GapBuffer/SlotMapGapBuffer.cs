using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    internal sealed class SlotMapGapBuffer<T>
    {
        public SlotMapGapBuffer(int initialCapacity = 16)
        {
            _buffer = new GapBuffer<T>(initialCapacity);
            _handles = new GapBufferSlotMap<T>(_buffer);
        }

        private readonly GapBuffer<T> _buffer;
        private readonly GapBufferSlotMap<T> _handles;

        public int Count => _buffer.Count;
        public int Capacity => _buffer.Capacity;

        /// <summary>Moves elements to a boundary in the original logical sequence, preserving their anchors.</summary>
        public void MoveRange(int sourceIndex, int destinationBoundary, int count) =>
            _handles.MoveRange(sourceIndex, destinationBoundary, count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InsertAt(int index, T item)
        {
            ValidateInsertionIndex(index);
            _buffer.Insert(index, item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAt(int index)
        {
            ValidateExistingIndex(index);
            _handles.RemoveAtAddress(_buffer.AddressOf(index));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetAt(int index)
        {
            ValidateExistingIndex(index);
            return _buffer[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAt(int index, T item)
        {
            ValidateExistingIndex(index);
            _buffer[index] = item;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GapBufferItemAnchor InsertTrackedAt(int index, T item)
        {
            ValidateInsertionIndex(index);
            return _handles.Insert(index, item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(GapBufferItemAnchor handle) =>
            _handles.Remove(handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(GapBufferItemAnchor handle) =>
            _handles.Get(handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetRef(GapBufferItemAnchor handle) =>
            ref _handles.GetRef(handle);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(GapBufferItemAnchor handle, T item) =>
            _handles.Set(handle, item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf(GapBufferItemAnchor anchor) =>
            _handles.IndexOf(anchor);

        internal GapBufferItemAnchor TrackAt(int index)
        {
            ValidateExistingIndex(index);
            return _handles.Track(_buffer.AddressOf(index));
        }

        internal GapBufferItemAnchor AnchorAt(int index)
        {
            ValidateExistingIndex(index);
            return _handles.AnchorAtAddress(_buffer.AddressOf(index));
        }

        public bool IsValidAnchor(GapBufferItemAnchor anchor) => _handles.IsValidAnchor(anchor);

        private void ValidateExistingIndex(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void ValidateInsertionIndex(int index)
        {
            if (index < 0 || index > Count) throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
