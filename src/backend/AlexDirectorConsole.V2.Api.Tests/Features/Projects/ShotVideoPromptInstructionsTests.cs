using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ShotVideoPromptInstructionsTests
{
    [Theory]
    [InlineData("minimax-h3")]
    [InlineData("MiniMax H3")]
    [InlineData("hailuo-h3")]
    public void Known_h3_models_use_h3_format(string model)
    {
        Assert.True(ShotVideoPromptInstructions.UsesMiniMaxH3Format(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unsupported-model")]
    public void Missing_or_unknown_models_use_current_default_format(string? model)
    {
        Assert.False(ShotVideoPromptInstructions.UsesMiniMaxH3Format(model));
    }
}