using System.Reflection;
using DotNetCompose.Runtime.Composer;

namespace DotNetCompose.Runtime.Tests;

public class ComposeHelpersTests
{
    public class ThrowingComposerProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidOperationException(
                $"Composer member '{targetMethod?.Name}' must not be used.");
        }
    }

    [Fact]
    public void GetReadonlyLambdaCreatesANewWrapperWithoutTouchingComposer()
    {
        IComposerContext context = DispatchProxy.Create<IComposerContext, ThrowingComposerProxy>();
        int factories = 0;
        int invocations = 0;
        Func<Delegate> factory = () =>
        {
            factories++;
            return (ComposableAction)((_, _, _) => invocations++);
        };

        ComposableLambdaWrapper first = ComposeHelpers.GetReadonlyLambda(context, 17, factory);
        ComposableLambdaWrapper second = ComposeHelpers.GetReadonlyLambda(context, 17, factory);

        Assert.NotSame(first, second);
        Assert.Equal(2, factories);
        first.Invoke(context, default, default);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public void GetReadonlyLambdaValidatesArguments()
    {
        IComposerContext context = DispatchProxy.Create<IComposerContext, ThrowingComposerProxy>();
        Assert.Throws<ArgumentNullException>(() => ComposeHelpers.GetReadonlyLambda(null!, 0, () => (Action)(() => { })));
        Assert.Throws<ArgumentNullException>(() => ComposeHelpers.GetReadonlyLambda(context, 0, null!));
    }

    [Fact]
    public void ComposableAttributeKeepsParameterlessConstructorAndExposesMode()
    {
        Assert.Equal(ComposableMode.Restartable, new ComposableAttribute().Mode);
        Assert.Equal(
            ComposableMode.ReadOnly,
            new ComposableAttribute(ComposableMode.ReadOnly).Mode);
    }
}
