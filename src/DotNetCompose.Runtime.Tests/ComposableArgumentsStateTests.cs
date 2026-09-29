namespace DotNetCompose.Runtime.Tests;

public class ComposableArgumentsStateTests
{
    [Fact]
    public void ForceDoesNotOverwriteParameterStates()
    {
        ComposableArgumentsState forced = ComposableArgumentsState.Forced(
            stackalloc byte[]
            {
                ComposableArgumentsState.Uncertain,
                ComposableArgumentsState.Different,
                ComposableArgumentsState.Same,
                ComposableArgumentsState.Static
            });

        Assert.True(forced.IsForced);
        Assert.Equal(ComposableArgumentsState.Uncertain, forced[0]);
        Assert.Equal(ComposableArgumentsState.Different, forced[1]);
        Assert.Equal(ComposableArgumentsState.Same, forced[2]);
        Assert.Equal(ComposableArgumentsState.Static, forced[3]);
        Assert.True(ComposableArgumentsState.Force.IsForced);
        Assert.Equal(ComposableArgumentsState.Uncertain, ComposableArgumentsState.Force[0]);
        Assert.False(ComposableArgumentsState.Empty.IsForced);
    }

    [Theory]
    [InlineData(ComposableArgumentsState.Uncertain, ComposableArgumentsState.Uncertain)]
    [InlineData(ComposableArgumentsState.Different, ComposableArgumentsState.Same)]
    [InlineData(ComposableArgumentsState.Same, ComposableArgumentsState.Same)]
    [InlineData(ComposableArgumentsState.Static, ComposableArgumentsState.Static)]
    public void NormalizeForRestartPreservesSlotOwnership(byte state, byte expected)
    {
        Assert.Equal(expected, ComposableArgumentsState.NormalizeForRestart(state));
    }
}
