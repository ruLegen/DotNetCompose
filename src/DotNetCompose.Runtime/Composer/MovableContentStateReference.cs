using DotNetCompose.Runtime.CompositionLocal;

namespace DotNetCompose.Runtime.Composer
{
    internal class MovableContentStateReference
    {
        public MovableContent<object?> Content { get; }
        public object? Parameter { get; }
        public SlotTable SlotStorage { get; }
        public GapAnchor Anchor { get; }
        public CompositionLocalMap Locals { get; }

        public MovableContentStateReference(
            MovableContent<object?> content,
            object? parameter,
            SlotTable slotStorage,
            GapAnchor anchor,
            CompositionLocalMap locals)
        {
            Content = content;
            Parameter = parameter;
            SlotStorage = slotStorage;
            Anchor = anchor;
            Locals = locals;
        }
    }
}
