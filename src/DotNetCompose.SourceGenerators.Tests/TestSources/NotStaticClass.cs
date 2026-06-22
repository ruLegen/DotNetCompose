using DotNetCompose.Runtime;

namespace DotNetCompose.Playground;
{
    internal partial class NotStaticClass
    {
        [Composable]
        public void ComposableTest(int i)
        {
        }

        [Composable]
        private void ComposableTest2(int i, [Composable] Action<int> action)
        {
            action.Invoke(i);
            action(i);
            action?.Invoke(i);
        }
    }
}
