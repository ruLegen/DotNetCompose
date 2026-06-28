using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests
{
    public class SnapshotPolicyTests
    {
        [Fact]
        public void StructuralEquality_EqualValues_ReturnsTrue()
        {
            var policy = StructuralPolicy<int>.Default;
            Assert.True(policy.Equivalent(42, 42));
        }

        [Fact]
        public void StructuralEquality_DifferentValues_ReturnsFalse()
        {
            var policy = StructuralPolicy<int>.Default;
            Assert.False(policy.Equivalent(1, 2));
        }

        [Fact]
        public void ReferentialEquality_SameRef_ReturnsTrue()
        {
            var policy = ReferentialPolicy<object>.Default;
            var obj = new object();
            Assert.True(policy.Equivalent(obj, obj));
        }

        [Fact]
        public void ReferentialEquality_DifferentRefs_ReturnsFalse()
        {
            var policy = ReferentialPolicy<object>.Default;
            Assert.False(policy.Equivalent(new object(), new object()));
        }

        [Fact]
        public void NeverEqual_Anything_ReturnsFalse()
        {
            var policy = NeverEqualPolicy<int>.Default;
            Assert.False(policy.Equivalent(0, 0));
            Assert.False(policy.Equivalent(1, 2));
        }
    }
}
