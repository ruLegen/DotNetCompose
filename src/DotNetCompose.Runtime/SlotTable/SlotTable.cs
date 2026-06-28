using System;
using System.Collections.Generic;
using System.Threading;

namespace DotNetCompose.Runtime
{
    internal sealed class SlotTable
    {
        internal const int MinGroupCapacity = 32;
        internal const int MinSlotCapacity = 64;

        private GroupRecord[] _groups;
        private int _groupsSize;
        private object?[] _slots;
        private int _slotsSize;
        private int _readers;
        private bool _writer;
        private int _version;
        private List<GapAnchor> _anchors = new List<GapAnchor>();

        public SlotTable()
        {
            _groups = new GroupRecord[MinGroupCapacity];
            _slots = new object[MinSlotCapacity];
            _groupsSize = 1;
            _slotsSize = 0;

            _groups[0] = new GroupRecord
            {
                Key = 0,
                Size = 1,
                ParentAnchor = -1,
                DataAnchor = 0,
            };
        }

        public int GroupsSize => _groupsSize;
        public int SlotsSize => _slotsSize;
        public int Version => _version;

        public int GroupSize(int index)
        {
            var groups = _groups;
            return groups[AnchorEncoder.GroupIndexToAddress(index, 0, 0)].Size;
        }

        public SlotReader OpenRead()
        {
            while (_writer)
                Thread.SpinWait(1);

            Interlocked.Increment(ref _readers);
            return new SlotReader(
                _groups, _groupsSize,
                _slots, _slotsSize);
        }

        internal void CloseRead()
        {
            Interlocked.Decrement(ref _readers);
        }

        internal SlotReader CreateReadSnapshot()
        {
            return new SlotReader(
                _groups, _groupsSize,
                _slots, _slotsSize);
        }

        public T Read<T>(Func<SlotReader, T> block)
        {
            while (_writer)
                Thread.SpinWait(1);

            Interlocked.Increment(ref _readers);
            try
            {
                var reader = new SlotReader(
                    _groups, _groupsSize,
                    _slots, _slotsSize);
                return block(reader);
            }
            finally
            {
                Interlocked.Decrement(ref _readers);
            }
        }

        public T Write<T>(Func<SlotWriter, T> block)
        {
            SpinWait spinner = default;
            while (Volatile.Read(ref _writer) || Volatile.Read(ref _readers) > 0)
                spinner.SpinOnce();

            Volatile.Write(ref _writer, true);
            Interlocked.Increment(ref _version);
            try
            {
                var writer = new SlotWriter(
                    _groups, _slots, _anchors,
                    _groupsSize, _slotsSize);
                var result = block(writer);
                writer.Close(normalClose: true);
                _groups = writer.Groups;
                _slots = writer.Slots;
                _groupsSize = writer.Size;
                _slotsSize = writer.SlotsSize;
                _anchors = writer.Anchors;
                return result;
            }
            finally
            {
                Volatile.Write(ref _writer, false);
            }
        }

        public void AddAnchor(GapAnchor anchor)
        {
            _anchors.Add(anchor);
        }

        public bool RemoveAnchor(GapAnchor anchor)
        {
            return _anchors.Remove(anchor);
        }
    }
}
