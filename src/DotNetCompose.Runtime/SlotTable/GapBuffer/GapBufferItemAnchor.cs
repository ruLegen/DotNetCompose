using System;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    internal record struct GapBufferItemAnchor(int Id, int Generation)
    {
        public static readonly GapBufferItemAnchor Empty = new(-1, -1);
    }
}
