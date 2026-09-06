using System;

namespace DotNetCompose.Runtime.SlotTable.GapBuffer
{
    public record struct GapBufferItemAnchor(int Id, int Generation)
    {
        public bool IsValid => Generation > 0;
    }
}
