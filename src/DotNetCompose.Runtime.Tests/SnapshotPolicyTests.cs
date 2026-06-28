using DotNetCompose.Runtime.Snapshots;

namespace DotNetCompose.Runtime.Tests
{
    public class SnapshotPolicyTests
    {
        [Fact]
        public void StructuralEquality_EqualValues_ReturnsTrue()
        {
            var policy = StructuralEqualityPolicy<int>.Default;
            Assert.True(policy.Equivalent(42, 42));
        }

        [Fact]
        public void StructuralEquality_DifferentValues_ReturnsFalse()
        {
            var policy = StructuralEqualityPolicy<int>.Default;
            Assert.False(policy.Equivalent(1, 2));
        }

        [Fact]
        public void ReferentialEquality_SameRef_ReturnsTrue()
        {
            var policy = ReferentialEqualityPolicy<object>.Instance;
            var obj = new object();
            Assert.True(policy.Equivalent(obj, obj));
        }

        [Fact]
        public void ReferentialEquality_DifferentRefs_ReturnsFalse()
        {
            var policy = ReferentialEqualityPolicy<object>.Instance;
            Assert.False(policy.Equivalent(new object(), new object()));
        }

        [Fact]
        public void NeverEqual_Anything_ReturnsFalse()
        {
            var policy = NeverEqualPolicy<int>.Instance;
            Assert.False(policy.Equivalent(0, 0));
            Assert.False(policy.Equivalent(1, 2));
        }
    }
}
