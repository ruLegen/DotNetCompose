using DotNetCompose.Runtime.SlotTable.GapBuffer;
using System;

namespace DotNetCompose.Runtime.SlotTable
{
    public partial class ComposerSlotTable
    {
        public ComposerSlotTable() { }

        /// <summary>Distinguishes an absent slot from a slot containing null.</summary>
        public static readonly object Empty = Composables.Empty;


        private readonly SlotMapGapBuffer<GroupRecord> _groups = new SlotMapGapBuffer<GroupRecord>();
        private readonly SlotMapGapBuffer<object?> _slots = new SlotMapGapBuffer<object?>();
        
        private int _readers = 0;
        private bool _writing = false;
        private int _version = 0;

        public int Size => _groups.Count;
        internal int Version => _version;
        internal GroupAnchor Wrap(GapBufferItemAnchor anchor) => anchor == GapBufferItemAnchor.Empty
            ? GroupAnchor.Empty : new GroupAnchor(this, anchor);

        internal GapBufferItemAnchor Resolve(GroupAnchor anchor, bool allowEmpty = false)
        {
            if (anchor.IsEmpty)
            {
                if (allowEmpty) return GapBufferItemAnchor.Empty;
                throw new InvalidOperationException("A group anchor is required.");
            }
            if (!ReferenceEquals(anchor.Owner, this))
                throw new ArgumentException("The group anchor belongs to another slot table.", nameof(anchor));
            if (!_groups.IsValidAnchor(anchor.Item))
                throw new InvalidOperationException("The group anchor is no longer valid.");
            return anchor.Item;
        }

        internal int IndexOf(GroupAnchor anchor) => _groups.IndexOf(Resolve(anchor));

        public Reader OpenReader()
        {
            if (_writing)
                throw new InvalidOperationException("Cannot open Reader when there is an opened writer");
            _readers++;
            return new Reader(this);
        }

        public Writer OpenWriter()
        {
            EnsureCanWrite();
            _writing=true;
            _version++;
            return new Writer(this);
        }

        internal void EnsureCanWrite()
        {
            if (_writing)
                throw new InvalidOperationException("Cannot start a writer when another writer is active");
            if (_readers > 0)
                throw new InvalidOperationException("Cannot start a writer when a reader is active");
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
            _writing = false;
        }
    }
}
