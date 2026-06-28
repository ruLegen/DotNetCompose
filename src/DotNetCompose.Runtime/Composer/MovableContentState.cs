namespace DotNetCompose.Runtime.Composer
{
    internal class MovableContentState
    {
        public SlotTable SlotStorage { get; }
        public int GroupCount { get; }

        public MovableContentState(SlotTable slotStorage, int groupCount)
        {
            SlotStorage = slotStorage;
            GroupCount = groupCount;
        }
    }
}
