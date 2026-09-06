namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public interface IStableGapBuffer<T>
    {
        int Count { get; }
        GapBufferItemAnchor InsertStable(int position, T item);
        void Remove(GapBufferItemAnchor handle);
        T Get(GapBufferItemAnchor handle);
        void Set(GapBufferItemAnchor handle, T item);
    }
}
