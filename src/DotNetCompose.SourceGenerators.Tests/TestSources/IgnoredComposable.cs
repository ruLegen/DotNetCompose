using DotNetCompose.Runtime;

namespace TestNs;

public static partial class TestClass
{
    public partial class Builders
    {
        public static void Ignored(DotNetCompose.Runtime.Composer.IComposerContext __ctx, DotNetCompose.Runtime.ComposableArgumentsState __changed, DotNetCompose.Runtime.ComposableArgumentsDefaultState __defaultParamState)
        {

        }
    }

    [Composable]
    [ComposableIgnore]
    public static void Ignored()
    {
    }

    [Composable]
    public static void NotIgnored()
    {
    }
}
