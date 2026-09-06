namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public interface IPhysicalGapBuffer<T>
    {
        int Count { get; }
        int Capacity { get; }
        int Insert(int position, T item);
        void Remove(int physicalIndex);
        T Get(int physicalIndex);
        void Set(int physicalIndex, T item);
    }
}
