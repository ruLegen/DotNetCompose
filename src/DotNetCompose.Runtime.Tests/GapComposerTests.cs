using Xunit;

namespace DotNetCompose.Runtime.Tests
{
    public class GapComposerTests
    {
        [Fact]
        public void ComposeContent_CreatesRoot()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) => { });

            Assert.Equal(1, composer.SlotTable.GroupsSize);
        }

        [Fact]
        public void StartEndGroup_InsertsGroup()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(42);
                composer.EndGroup();
            });
            composer.Drain();

            Assert.Equal(2, composer.SlotTable.GroupsSize);

            composer.SlotTable.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(42, reader.GroupKey);
                Assert.False(reader.IsNode);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void NestedGroups_CorrectStructure()
        {
            var composer = new Composer.GapComposer();
            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.StartGroup(2);
                composer.EndGroup();
                composer.EndGroup();
            });
            composer.Drain();

            Assert.Equal(3, composer.SlotTable.GroupsSize);

            composer.SlotTable.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(1, reader.GroupKey);
                reader.StartGroup();
                Assert.Equal(2, reader.GroupKey);
                reader.EndGroup();
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void Changed_FirstTime_ReturnsTrue()
        {
            var composer = new Composer.GapComposer();
            bool changed = false;

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                changed = composer.Changed(42);
                composer.EndGroup();
            });

            Assert.True(changed);
        }

        [Fact]
        public void Changed_SecondTimeSameValue_ReturnsFalse()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.Changed(42);
                composer.EndGroup();
            });
            composer.Drain();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                bool changed = composer.Changed(42);
                Assert.False(changed);
                composer.EndGroup();
            });
        }

        [Fact]
        public void Changed_DifferentValue_ReturnsTrue()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.Changed(42);
                composer.EndGroup();
            });
            composer.Drain();

            bool changed = false;
            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                changed = composer.Changed(99);
                composer.EndGroup();
            });

            Assert.True(changed);
        }

        [Fact]
        public void CreateNode_InsertsNodeGroup()
        {
            var composer = new Composer.GapComposer();
            string nodeValue = "test";

            composer.ComposeContent((_, _, _) =>
            {
                composer.CreateNode(() => nodeValue);
                composer.EndGroup();
            });
            composer.Drain();

            Assert.Equal(2, composer.SlotTable.GroupsSize);

            composer.SlotTable.Read(reader =>
            {
                reader.StartGroup();
                Assert.True(reader.IsNode);
                Assert.Equal("test", reader.GroupNode);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void ApplyNode_CallsAction()
        {
            var composer = new Composer.GapComposer();
            string? applied = null;

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.ApplyNode<string>(node =>
                {
                    applied = node;
                }, "hello");
                composer.EndGroup();
            });

            Assert.Equal("hello", applied);
        }

        [Fact]
        public void RememberedValue_Initially_ReturnsEmpty()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                var rv = composer.RememberedValue();
                Assert.Same(ComposerStatics.Empty, rv);
                composer.EndGroup();
            });
        }

        [Fact]
        public void MultipleComposeSessions_Accumulate()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.EndGroup();
            });
            composer.Drain();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.Changed(100);
                composer.EndGroup();
            });
            composer.Drain();

            Assert.Equal(2, composer.SlotTable.GroupsSize);

            composer.SlotTable.Read(reader =>
            {
                reader.StartGroup();
                Assert.Equal(100, reader.Get(0));
                Assert.Equal(1, reader.GroupKey);
                reader.EndGroup();
                return 0;
            });
        }

        [Fact]
        public void SkipToGroupEnd_SkipsGroup()
        {
            var composer = new Composer.GapComposer();

            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.StartGroup(2);
                composer.EndGroup();
                composer.EndGroup();
            });
            composer.Drain();

            bool saw2 = false;
            composer.ComposeContent((_, _, _) =>
            {
                composer.StartGroup(1);
                composer.SkipToGroupEnd();
                composer.EndGroup();
                saw2 = true;
            });

            Assert.True(saw2);
        }
    }
}
