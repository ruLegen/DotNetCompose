using System;

namespace DotNetCompose.Runtime
{
    internal static class AnchorEncoder
    {
        public static int GroupIndexToAddress(int index, int gapStart, int gapLen)
        {
            if (index < gapStart)
                return index;
            return index + gapLen;
        }

        public static int DataIndexToAddress(int index, int gapStart, int gapLen)
        {
            if (index < gapStart)
                return index;
            return index + gapLen;
        }

        public static int DataIndexToDataAnchor(int index, int gapStart, int gapLen, int capacity)
        {
            int slotsSize = capacity - gapLen;
            if (index > gapStart)
                return -((slotsSize) - index + 1);
            return index;
        }

        public static int DataAnchorToIndex(int anchor, int gapLen, int capacity)
        {
            int slotsSize = capacity - gapLen;
            if (anchor < 0)
                return slotsSize + anchor + 1;
            return anchor;
        }

        private const int ParentAnchorPivot = -2;

        public static int ParentIndexToAnchor(int index, int gapStart, int capacity, int gapLen)
        {
            int size = capacity - gapLen;
            if (index < gapStart)
                return index;
            return -(size - index - ParentAnchorPivot);
        }

        public static int ParentAnchorToIndex(int anchor, int capacity, int gapLen)
        {
            int size = capacity - gapLen;
            if (anchor > ParentAnchorPivot)
                return anchor;
            return size + anchor - ParentAnchorPivot;
        }

        public static int GapAnchorIndex(GapAnchor a, int size)
        {
            return a.Location < 0 ? size + a.Location : a.Location;
        }

        public static int SlotAnchor(int addr, GroupRecord[] groups)
        {
            return groups[addr].DataAnchor + PopCount((uint)(((int)groups[addr].Flags) >> 3));
        }

        private static int PopCount(uint value)
        {
            value = value - ((value >> 1) & 0x55555555);
            value = (value & 0x33333333) + ((value >> 2) & 0x33333333);
            return (int)((value + (value >> 4) & 0x0F0F0F0F) * 0x01010101) >> 24;
        }
    }
}
