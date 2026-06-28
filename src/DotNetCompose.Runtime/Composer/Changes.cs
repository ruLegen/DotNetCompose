namespace DotNetCompose.Runtime.Composer
{
    internal abstract class Changes
    {
        public abstract void Clear();
        public abstract bool IsEmpty();
        public abstract void Execute(
            SlotTable slotStorage,
            IApplier<object> applier,
            RememberManager rememberManager
        );
    }
}
