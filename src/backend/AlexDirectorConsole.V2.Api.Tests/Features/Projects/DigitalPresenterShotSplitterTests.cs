using AlexDirectorConsole.V2.Api.Features.Projects.DigitalPresenters;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class DigitalPresenterShotSplitterTests
{
    [Fact]
    public void Split_UsesH3SpeechRateAndSentenceBoundaries()
    {
        var shots = DigitalPresenterShotSplitter.Split(
            "签劳动合同时，试用期不是想约多久就能约多久。遇到争议，请保存合同和沟通证据。");

        Assert.Equal(2, shots.Count);
        Assert.Equal(7, shots[0].DurationSeconds);
        Assert.All(shots, shot => Assert.InRange(shot.DurationSeconds, 4, 15));
    }

    [Fact]
    public void Split_NeverProducesShotLongerThanH3Limit()
    {
        var shots = DigitalPresenterShotSplitter.Split(new string('法', 120));

        Assert.True(shots.Count >= 3);
        Assert.All(shots, shot =>
        {
            Assert.InRange(shot.Characters, 1, 52);
            Assert.InRange(shot.DurationSeconds, 4, 15);
        });
    }
}