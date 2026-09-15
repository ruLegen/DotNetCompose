using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using static DotNetCompose.Runtime.SlotTable.ComposerSlotTable;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        public ComposerSlotTable() { }


        private readonly SlotMapGapBuffer<GroupRecord> _groups = new SlotMapGapBuffer<GroupRecord>();
        private readonly SlotMapGapBuffer<object?> _slots = new SlotMapGapBuffer<object?>();
        
        private int _readers = 0;
        private bool _writing = false;
        private int _version = 0;

        public Reader OpenReader()
        {
            if (_writing)
                throw new InvalidOperationException("Cannot open Reader when there is an opened writer");
            _readers++;
            return new Reader(this);
        }

        public Writer OpenWriter()
        {
            if (_writing)
                throw new InvalidOperationException("Cannot start a writer when another writer is active");
            if (_readers > 0)
                throw new InvalidOperationException("Cannot start a writer when a reader is active");
            _writing=true;
            _version++;
            return new Writer(this);
        }


        private void CloseReader(Reader reader)
        {
            if (reader.Table != this || _readers <= 0)
                throw new InvalidOperationException("Unexpected Reader close");
            _readers--;
        }
        private void CloseWriter(Writer writer)
        {
            if (writer.Table != this || _writing == false)
                throw new InvalidOperationException("Unexpected Writer close");
            // apply data to table
            _writing = false;
        }
    }
}
