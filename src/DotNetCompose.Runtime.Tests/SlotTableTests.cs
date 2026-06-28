using Xunit;

namespace DotNetCompose.Runtime.Tests
{
    public class SlotTableTests
    {
        [Fact]
        public void Constructor_CreatesRootGroup()
        {
            var table = new SlotTable();
            Assert.Equal(1, table.GroupsSize);
            Assert.Equal(0, table.SlotsSize);
        }

        [Fact]
        public void Read_ReturnsRootInfo()
        {
            var table = new SlotTable();
            table.Read(reader =>
            {
                Assert.Equal(0, reader.CurrentGroup);
                Assert.Equal(1, reader.GroupSize);
                Assert.Equal(0, reader.GroupKey);
                Assert.False(reader.IsNode);
                Assert.True(reader.IsRoot);
                return 0;
            });
        }

        [Fact]
        public void Write_InsertGroup_UpdatesSize()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.EndGroup();
                return 0;
            });
            Assert.Equal(2, table.GroupsSize);
        }

        [Fact]
        public void Write_StartEndGroup_Nested()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.StartGroup(2);
                writer.EndGroup();
                writer.EndGroup();
                return 0;
            });
            Assert.Equal(3, table.GroupsSize);
        }

        [Fact]
        public void Read_AfterWrite_SeesCorrectStructure()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.StartNode(2, "nodeVal", "keyVal");
                writer.EndGroup();
                writer.EndGroup();
                return 0;
            });

            table.Read(reader =>
            {
                Assert.Equal(3, reader.GroupSize);
                Assert.Equal(2, reader.GroupSizeAt(1));

                reader.StartGroup();
                Assert.Equal(1, reader.GroupKey);
                Assert.Equal(2, reader.GroupSize);

                reader.StartGroup();
                Assert.Equal(2, reader.GroupKey);
                Assert.True(reader.IsNode);
                Assert.Equal("keyVal", reader.Get(0));
                Assert.Equal("nodeVal", reader.Get(1));
                Assert.Equal("nodeVal", reader.GroupNode);
                reader.EndGroup();

                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Read_StartEndGroup_Navigation()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.StartGroup(2);
                writer.EndGroup();
                writer.StartGroup(3);
                writer.EndGroup();
                writer.EndGroup();
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(1, reader.GroupKey);

                reader.StartGroup();
                Assert.Equal(2, reader.GroupKey);
                reader.EndGroup();

                Assert.False(reader.IsGroupEnd);
                reader.StartGroup();
                Assert.Equal(3, reader.GroupKey);
                reader.EndGroup();

                Assert.True(reader.IsGroupEnd);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Read_SkipGroup_ReturnsNodeCount()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.StartNode(2, "a", null);
                writer.EndGroup();
                writer.StartNode(3, "b", null);
                writer.EndGroup();
                writer.EndGroup();
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                int nc = reader.SkipGroup();
                Assert.Equal(2, nc);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Write_RemoveGroup_ShrinksSize()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.EndGroup();
                return 0;
            });
            Assert.Equal(2, table.GroupsSize);

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(1));
                writer.RemoveGroup();
                return 0;
            });
            Assert.Equal(1, table.GroupsSize);
        }

        [Fact]
        public void Write_MoveGroup_WithinSiblings()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartGroup(10);
                writer.EndGroup();
                writer.StartGroup(20);
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(1));
                writer.MoveGroup(1);
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(20, reader.GroupKey);
                reader.SkipGroup();
                Assert.Equal(10, reader.GroupKey);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Write_MoveGroup_DataSlotsPreserved()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                writer.StartNode(1, "nodeA", null);
                writer.EndGroup();
                writer.StartNode(2, "nodeB", null);
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(2));
                writer.MoveGroup(1);
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(2, reader.GroupKey);
                Assert.Equal("nodeB", reader.GroupNode);
                reader.SkipGroup();
                Assert.Equal(1, reader.GroupKey);
                Assert.Equal("nodeA", reader.GroupNode);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void GapAnchor_SurvivesInsertBefore()
        {
            var table = new SlotTable();
            GapAnchor anchor = null!;

            table.Write(writer =>
            {
                writer.StartGroup(1);
                anchor = writer.Anchor();
                writer.EndGroup();
                return 0;
            });

            Assert.True(anchor.Valid);

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(0));
                writer.StartGroup(0);
                writer.EndGroup();
                return 0;
            });

            Assert.True(anchor.Valid);
        }

        [Fact]
        public void GapAnchor_InvalidatedOnRemove()
        {
            var table = new SlotTable();
            GapAnchor anchor = null!;

            table.Write(writer =>
            {
                writer.StartGroup(1);
                anchor = writer.Anchor();
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(1));
                writer.RemoveGroup();
                return 0;
            });

            Assert.False(anchor.Valid);
        }

        [Fact]
        public void GroupRecord_Flags_Node()
        {
            var g = new GroupRecord { Flags = GroupFlags.Node };
            Assert.True(g.IsNode);
            Assert.False(g.HasObjectKey);
            Assert.False(g.HasAux);
            Assert.False(g.IsMarked);
            Assert.False(g.ContainsMarked);
        }

        [Fact]
        public void GroupRecord_Flags_ObjectKey()
        {
            var g = new GroupRecord { Flags = GroupFlags.ObjectKey };
            Assert.True(g.HasObjectKey);
            Assert.False(g.IsNode);
        }

        [Fact]
        public void GroupRecord_Flags_Aux()
        {
            var g = new GroupRecord { Flags = GroupFlags.Aux };
            Assert.True(g.HasAux);
        }

        [Fact]
        public void GroupRecord_Flags_Mark()
        {
            var g = new GroupRecord { Flags = GroupFlags.Mark };
            Assert.True(g.IsMarked);
        }

        [Fact]
        public void GroupRecord_Flags_ContainsMark()
        {
            var g = new GroupRecord { Flags = GroupFlags.ContainsMark };
            Assert.True(g.ContainsMarked);
        }

        [Fact]
        public void ConcurrentReaders_Allowed()
        {
            var table = new SlotTable();
            var results = new List<int>();
            object lockObj = new object();

            var t1 = Task.Run(() =>
                table.Read(r => { lock (lockObj) results.Add(0); Thread.Sleep(50); return 0; }));

            var t2 = Task.Run(() =>
                table.Read(r => { lock (lockObj) results.Add(1); Thread.Sleep(50); return 0; }));

            Task.WaitAll(t1, t2);
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void WriterExclusiveAccess_BlocksReader()
        {
            var table = new SlotTable();
            var readerRan = false;

            var writerTask = Task.Run(() =>
            {
                table.Write(writer =>
                {
                    Thread.Sleep(100);
                    writer.StartGroup(1);
                    writer.EndGroup();
                    return 0;
                });
            });

            Thread.Sleep(10);
            var readerTask = Task.Run(() =>
            {
                table.Read(r => { readerRan = true; return 0; });
            });

            Task.WaitAll(writerTask, readerTask);
            Assert.True(readerRan);
            Assert.Equal(2, table.GroupsSize);
        }

        [Fact]
        public void GrowGroups_GapExpands()
        {
            var table = new SlotTable();
            table.Write(writer =>
            {
                for (int i = 0; i < 100; i++)
                {
                    writer.StartGroup(i + 1);
                    writer.EndGroup();
                }
                return 0;
            });
            Assert.Equal(101, table.GroupsSize);

            table.Read(reader =>
            {
                reader.StartGroup();
                for (int i = 0; i < 100; i++)
                {
                    Assert.Equal(i + 1, reader.GroupKey);
                    Assert.Equal(1, reader.GroupSize);
                    reader.SkipGroup();
                }
                Assert.True(reader.IsGroupEnd);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void TreeStructure_ValidAfterMultipleInserts()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                writer.StartGroup(1);
                writer.StartGroup(10);
                writer.StartGroup(100);
                writer.EndGroup();
                writer.EndGroup();
                writer.StartGroup(20);
                writer.EndGroup();
                writer.EndGroup();
                return 0;
            });

            table.Read(reader =>
            {
                Assert.Equal(5, reader.GroupSize);

                reader.StartGroup();
                Assert.Equal(1, reader.GroupKey);
                Assert.Equal(4, reader.GroupSize);

                reader.StartGroup();
                Assert.Equal(10, reader.GroupKey);

                reader.StartGroup();
                Assert.Equal(100, reader.GroupKey);
                reader.EndGroup();

                reader.EndGroup();

                Assert.False(reader.IsGroupEnd);
                reader.StartGroup();
                Assert.Equal(20, reader.GroupKey);
                reader.EndGroup();

                Assert.True(reader.IsGroupEnd);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Writer_Update_ReturnsPreviousValue()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                writer.StartNode(1, "old", null);
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(1));
                object? prev = writer.Update("new");
                Assert.Equal("old", prev);
                return 0;
            });
        }

        [Fact]
        public void Writer_Skip_SkipsSlot()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                writer.StartNode(1, "val1", null);
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(1));
                object? skipped = writer.Skip();
                Assert.Null(skipped);
                object? prev = writer.Update("val2");
                Assert.Equal("val1", prev);
                return 0;
            });
        }

        [Fact]
        public void Reader_Next_AdvancesThroughSlots()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                writer.StartNode(1, "node", "key");
                writer.EndGroup();
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal("key", reader.Next());
                Assert.Equal("node", reader.Next());
                Assert.Null(reader.Next());
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Writer_MoveGroup_SiblingOrderPreservesSlots()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                writer.StartNode(1, "A", null);
                writer.EndGroup();
                writer.StartNode(2, "B", null);
                writer.EndGroup();
                writer.StartNode(3, "C", null);
                writer.EndGroup();
                return 0;
            });

            table.Write(writer =>
            {
                writer.Seek(writer.Anchor(2));
                writer.MoveGroup(-1);
                return 0;
            });

            table.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(2, reader.GroupKey);
                Assert.Equal("B", reader.GroupNode);
                reader.SkipGroup();
                Assert.Equal(1, reader.GroupKey);
                Assert.Equal("A", reader.GroupNode);
                reader.SkipGroup();
                Assert.Equal(3, reader.GroupKey);
                Assert.Equal("C", reader.GroupNode);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Stress_LargeBatchInsert_AllSizesCorrect()
        {
            var table = new SlotTable();

            table.Write(writer =>
            {
                for (int i = 0; i < 200; i++)
                {
                    writer.StartGroup(i);
                    writer.EndGroup();
                }
                return 0;
            });

            Assert.Equal(201, table.GroupsSize);

            table.Read(reader =>
            {
                reader.StartGroup();
                int count = 0;
                while (!reader.IsGroupEnd)
                {
                    Assert.Equal(count, reader.GroupKey);
                    Assert.Equal(1, reader.GroupSize);
                    count++;
                    reader.SkipGroup();
                }
                Assert.Equal(200, count);
                reader.EndGroup();
                return 0;
            });
        }

    }
}
