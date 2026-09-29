namespace DotNetCompose.Runtime.SlotTable
{
    /// <summary>A remaining child group returned by Reader.ExtractKeys, in sibling order.</summary>
    public sealed class KeyInfo
    {
        public KeyInfo(int key, object? objectKey, int location, int nodes, int index)
        {
            Key = key;
            ObjectKey = objectKey;
            Location = location;
            Nodes = nodes;
            Index = index;
        }

        public int Key { get; }
        public object? ObjectKey { get; }
        public int Location { get; }
        public int Nodes { get; }
        public int Index { get; }
    }
}
