
namespace DotNetCompose.Runtime
{
    public ref struct ComposableLambdaWrapper
    {
        public ComposableLambdaWrapper(ComposableAction? action)
        {
            _action = action;
        }
        private ComposableAction? _action;

        public void Invoke(int groupId)
        {
            if(_action != null)
            {
                IComposeContext? ctx = ComposeScope.GetCurrentContext();
                ctx?.StartGroup(groupId);
                _action?.Invoke();
                ctx?.EndGroup(groupId);
            }
        }

        public static implicit operator ComposableLambdaWrapper(ComposableAction? b) 
            => new ComposableLambdaWrapper(b);
        public static ComposableLambdaWrapper FromComposableAction(ComposableAction action) 
            => new ComposableLambdaWrapper(action);

    }
}
