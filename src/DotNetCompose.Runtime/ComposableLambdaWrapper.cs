
namespace DotNetCompose.Runtime
{
    public ref struct ComposableLambdaWrapper
    {
        public ComposableLambdaWrapper(ComposableAction? action)
        {
            _action = action;
        }
        private ComposableAction? _action;

        public void Invoke(int groupId) => _action?.Invoke();

        public static implicit operator ComposableLambdaWrapper(ComposableAction? b) 
            => new ComposableLambdaWrapper(b);

    }
}
