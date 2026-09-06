namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public class SlotMapGapBuffer<T> : IPhysicalGapBuffer<T>, IStableGapBuffer<T>
    {
        private readonly GapBuffer<T> _buffer;
        private readonly GapBufferSlotMap<T> _handles;

        public SlotMapGapBuffer(int initialCapacity = 16)
        {
            _buffer = new GapBuffer<T>();
            _handles = new GapBufferSlotMap<T>(_buffer, initialCapacity);
        }

        public int Count => _buffer.Count;
        public int Capacity => _buffer.Capacity;

        public int Insert(int position, T item) =>
            _buffer.Insert(position, item);

        public void Remove(int physicalIndex) =>
            _buffer.RemoveAtPhysical(physicalIndex);

        public T Get(int physicalIndex) =>
            _buffer.GetAtPhysical(physicalIndex);

        public void Set(int physicalIndex, T item) =>
            _buffer.SetAtPhysical(physicalIndex, item);

        public GapBufferItemAnchor InsertStable(int position, T item) =>
            _handles.Insert(position, item);

        public void Remove(GapBufferItemAnchor handle) =>
            _handles.Remove(handle);

        public T Get(GapBufferItemAnchor handle) =>
            _handles.Get(handle);

        public void Set(GapBufferItemAnchor handle, T item) =>
            _handles.Set(handle, item);
    }
}
