namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public interface IAddressableGapBuffer<T>
    {
        int Count { get; }
        int Capacity { get; }
        int Insert(int address, T item);
        void Remove(int address);
        T Get(int address);
        void Set(int address, T item);
    }
}
