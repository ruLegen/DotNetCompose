using System;
using DotNetCompose.Runtime.SlotTable.GapBuffer;

namespace DotNetCompose.Runtime.Tests
{
    public class GapBufferTests
    {
        // ========== GapBufferItemAnchor ==========

        [Fact]
        public void Anchor_EqualIdsAndGens_AreEqual()
        {
            var a = new GapBufferItemAnchor(1, 1);
            var b = new GapBufferItemAnchor(1, 1);
            Assert.Equal(a, b);
            Assert.True(a == b);
        }

        [Fact]
        public void Anchor_DifferentIds_AreNotEqual()
        {
            var a = new GapBufferItemAnchor(1, 1);
            var b = new GapBufferItemAnchor(2, 1);
            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }

        [Fact]
        public void Anchor_DifferentGens_AreNotEqual()
        {
            var a = new GapBufferItemAnchor(1, 1);
            var b = new GapBufferItemAnchor(1, 2);
            Assert.NotEqual(a, b);
            Assert.True(a != b);
        }

        [Fact]
        public void Anchor_GenerationZero_IsInvalid()
        {
            var anchor = new GapBufferItemAnchor(0, 0);
            Assert.False(anchor.IsValid);
        }

        [Fact]
        public void Anchor_GenerationPositive_IsValid()
        {
            var anchor = new GapBufferItemAnchor(1, 1);
            Assert.True(anchor.IsValid);
        }

        // ========== GapBufferSlotMap: Basic ==========

        [Fact]
        public void SlotMap_InsertAndGet()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r = map.Insert("hello");
            Assert.True(r.IsValid);
            Assert.Equal("hello", map.Get(r));
        }

        [Fact]
        public void SlotMap_MultipleInserts()
        {
            var gap = new GapBuffer<int>();
            var map = new GapBufferSlotMap<int>(gap);

            var r1 = map.Insert(10);
            var r2 = map.Insert(20);
            var r3 = map.Insert(30);

            Assert.Equal(10, map.Get(r1));
            Assert.Equal(20, map.Get(r2));
            Assert.Equal(30, map.Get(r3));
            Assert.Equal(3, map.Count);
        }

        [Fact]
        public void SlotMap_RemoveAndReuse()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r1 = map.Insert("a");
            var r2 = map.Insert("b");
            map.Remove(r1);

            Assert.Throws<InvalidOperationException>(() => map.Get(r1));
            Assert.Equal("b", map.Get(r2));

            var r3 = map.Insert("c");
            Assert.True(r3.IsValid);
            Assert.Equal("c", map.Get(r3));
            Assert.NotEqual(r1, r3);
        }

        [Fact]
        public void SlotMap_StaleReferenceDetection()
        {
            var gap = new GapBuffer<int>();
            var map = new GapBufferSlotMap<int>(gap);

            var r1 = map.Insert(42);
            map.Remove(r1);

            Assert.Throws<InvalidOperationException>(() => map.Get(r1));
        }

        // ========== GapBufferSlotMap: Insert at Position ==========

        [Fact]
        public void SlotMap_InsertAtBeginning()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r1 = map.Insert("b");
            var r2 = map.Insert("c");
            var r0 = map.Insert(0, "a");

            Assert.Equal("a", map.Get(r0));
            Assert.Equal("b", map.Get(r1));
            Assert.Equal("c", map.Get(r2));
        }

        [Fact]
        public void SlotMap_InsertAtMiddle()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r0 = map.Insert("a");
            var r2 = map.Insert("c");
            var r1 = map.Insert(1, "b");

            Assert.Equal("a", map.Get(r0));
            Assert.Equal("b", map.Get(r1));
            Assert.Equal("c", map.Get(r2));
        }

        [Fact]
        public void SlotMap_InsertAtEnd()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r0 = map.Insert("a");
            var r1 = map.Insert(1, "b");

            Assert.Equal("a", map.Get(r0));
            Assert.Equal("b", map.Get(r1));
        }

        // ========== GapBufferSlotMap: Set ==========

        [Fact]
        public void SlotMap_SetByHandle()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r = map.Insert("old");
            Assert.Equal("old", map.Get(r));

            map.Set(r, "new");
            Assert.Equal("new", map.Get(r));
        }

        // ========== GapBufferSlotMap: MoveGap ==========

        [Fact]
        public void SlotMap_MoveGapBackward()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r0 = map.Insert("item0");
            var r1 = map.Insert("item1");
            var r2 = map.Insert("item2");
            var r3 = map.Insert("item3");
            var r4 = map.Insert("item4");

            var rNew = map.Insert(0, "NEW_AT_START");

            Assert.Equal("item0", map.Get(r0));
            Assert.Equal("item1", map.Get(r1));
            Assert.Equal("item2", map.Get(r2));
            Assert.Equal("item3", map.Get(r3));
            Assert.Equal("item4", map.Get(r4));
            Assert.Equal("NEW_AT_START", map.Get(rNew));
        }

        [Fact]
        public void SlotMap_MoveGapForward()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r0 = map.Insert("a");
            var r1 = map.Insert("b");
            var r2 = map.Insert("c");

            map.Get(r2);

            var rNew = map.Insert(0, "Z");

            Assert.Equal("a", map.Get(r0));
            Assert.Equal("b", map.Get(r1));
            Assert.Equal("c", map.Get(r2));
            Assert.Equal("Z", map.Get(rNew));
        }

        [Fact]
        public void SlotMap_InsertCausesResize()
        {
            var gap = new GapBuffer<int>();
            var map = new GapBufferSlotMap<int>(gap);

            var anchors = new GapBufferItemAnchor[12];
            for (int i = 0; i < 12; i++)
                anchors[i] = map.Insert(i * 10);

            var rExtra = map.Insert(999);

            for (int i = 0; i < 12; i++)
                Assert.Equal(i * 10, map.Get(anchors[i]));
            Assert.Equal(999, map.Get(rExtra));
        }

        // ========== GapBufferSlotMap: Batch ==========

        [Fact]
        public void SlotMap_BatchInsert()
        {
            var gap = new GapBuffer<int>();
            var map = new GapBufferSlotMap<int>(gap);

            var anchors = new GapBufferItemAnchor[5];
            for (int i = 0; i < 5; i++)
                anchors[i] = map.Insert(i);

            for (int i = 0; i < 5; i++)
                Assert.Equal(i, map.Get(anchors[i]));
        }

        [Fact]
        public void SlotMap_RemoveMiddle()
        {
            var gap = new GapBuffer<string>();
            var map = new GapBufferSlotMap<string>(gap);

            var r0 = map.Insert("x");
            var r1 = map.Insert("y");
            var r2 = map.Insert("z");

            map.Remove(r1);

            Assert.Equal("x", map.Get(r0));
            Assert.Equal("z", map.Get(r2));
            Assert.Throws<InvalidOperationException>(() => map.Get(r1));
        }

        // ========== GapBuffer: InsertRange ==========

        [Fact]
        public void GapBuffer_InsertRange()
        {
            var gap = new GapBuffer<int>();
            gap.AddRange(new[] { 10, 20, 30 });

            Assert.Equal(3, gap.Count);
            Assert.Equal(10, gap[0]);
            Assert.Equal(20, gap[1]);
            Assert.Equal(30, gap[2]);
        }

        [Fact]
        public void GapBuffer_InsertRangeAtMiddle()
        {
            var gap = new GapBuffer<int>();
            gap.AddRange(new[] { 1, 2, 5, 6 });
            gap.InsertRange(2, new[] { 3, 4 });

            Assert.Equal(6, gap.Count);
            for (int i = 0; i < 6; i++)
                Assert.Equal(i + 1, gap[i]);
        }

        // ========== SlotMapGapBuffer ==========

        [Fact]
        public void SlotMapGapBuffer_InsertAndGet()
        {
            var smb = new SlotMapGapBuffer<int>();
            var r = smb.InsertStable(0, 42);
            Assert.Equal(42, smb.Get(r));
            Assert.Equal(1, smb.Count);
        }

        [Fact]
        public void SlotMapGapBuffer_StableRefs()
        {
            var smb = new SlotMapGapBuffer<string>();
            var r0 = smb.InsertStable(0, "a");
            var r1 = smb.InsertStable(1, "b");
            var r2 = smb.InsertStable(2, "c");

            var rNew = smb.InsertStable(0, "Z");

            Assert.Equal("a", smb.Get(r0));
            Assert.Equal("b", smb.Get(r1));
            Assert.Equal("c", smb.Get(r2));
            Assert.Equal("Z", smb.Get(rNew));
        }

        [Fact]
        public void SlotMapGapBuffer_MixedAccess()
        {
            var smb = new SlotMapGapBuffer<int>();

            smb.Insert(0, 10);
            smb.Insert(1, 20);
            var r = smb.InsertStable(2, 30);

            Assert.Equal(10, smb.Get(0));
            Assert.Equal(20, smb.Get(1));
            Assert.Equal(30, smb.Get(r));

            smb.Set(r, 99);
            Assert.Equal(99, smb.Get(r));
        }
    }
}
