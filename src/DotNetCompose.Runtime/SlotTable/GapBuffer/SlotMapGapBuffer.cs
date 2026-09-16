using System;
using System.Runtime.CompilerServices;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public sealed class SlotMapGapBuffer<T> : IAddressableGapBuffer<T>, IStableGapBuffer<T>
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
 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Insert(int position, T item) =>
            _buffer.Insert(position, item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int physicalIndex) =>
            _handles.RemoveAtAddress(physicalIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int physicalIndex) =>
            _buffer.GetAtAddress(physicalIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int physicalIndex, T item) =>
            _buffer.SetAtAddress(physicalIndex, item);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GapBufferItemAnchor InsertStable(int position, T item) =>
            _handles.Insert(position, item);

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
        [MethodImpl (MethodImplOptions.AggressiveInlining)]
        public int GetIndexOfAnchor(GapBufferItemAnchor anchor) => 
            _handles.IndexOf(anchor);

        internal GapBufferItemAnchor Track(int slotStart) =>
            _handles.Track(slotStart);

        internal GapBufferItemAnchor GetAnchorAtIndex(int index) =>
            _handles.AnchorAtAddress(_buffer.AddressOf(index));

        public bool IsValidAnchor(GapBufferItemAnchor anchor) => _handles.IsValidAnchor(anchor);

        internal int GetAddressOfIndex(int index) =>
            _buffer.AddressOf(index);   


        internal void Reserve(int index, int count) =>
            _buffer.Reserve(index,count);   
    }
}
