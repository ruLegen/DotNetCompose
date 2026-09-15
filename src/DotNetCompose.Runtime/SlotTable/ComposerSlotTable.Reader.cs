using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        public class Reader(ComposerSlotTable table)
        {
            internal readonly ComposerSlotTable Table = table;

            private readonly SlotMapGapBuffer<GroupRecord> _groups = table._groups;
            private readonly SlotMapGapBuffer<object?> _slots = table._slots;

            


            public void Close()
            {
                Table.CloseReader(this);
            }
        }
    }
}
