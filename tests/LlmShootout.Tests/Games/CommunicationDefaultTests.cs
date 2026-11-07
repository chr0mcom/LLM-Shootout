using LlmShootout.Arena.Games;
using LlmShootout.Games.SplitOrSteal;
using LlmShootout.Tests.TestGames;

namespace LlmShootout.Tests.Games;

public sealed class CommunicationDefaultTests
{
    [Fact]
    public void A_game_without_a_declared_default_has_messages_off()
    {
        IGameDefinition definition = new NumberGuessDefinition();
        Assert.False(definition.DefaultCommunication);
        Assert.False(new NumberGuessDefinition().ResolveCommunication(null));
    }

    [Fact]
    public void Split_or_steal_keeps_its_default_of_messages_on()
    {
        Assert.True(new SplitOrStealDefinition().DefaultCommunication);
        Assert.True(new SplitOrStealDefinition().ResolveCommunication(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_explicit_setting_wins_over_the_default_of_every_game(bool requested)
    {
        Assert.Equal(requested, new NumberGuessDefinition().ResolveCommunication(requested));
        Assert.Equal(requested, new SplitOrStealDefinition().ResolveCommunication(requested));
    }

    [Fact]
    public void Resolving_needs_a_definition()
    {
        Assert.Throws<ArgumentNullException>(() => GameDefinitionExtensions.ResolveCommunication(null!, true));
    }
}
