using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using AlexDirectorConsole.V2.Api.Features.Projects;
using AlexDirectorConsole.V2.Api.Features.Projects.DigitalPresenters;
using AlexDirectorConsole.V2.Api.Tests.Infrastructure;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class DigitalPresenterEndpointTests(V2ApiFactory factory)
    : IClassFixture<V2ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Presenter_upload_and_episode_save_persist_media_and_split_shots()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var image = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
        var voice = "RIFF0000WAVE"u8.ToArray();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("法务讲解员"), "name" },
            { new ByteArrayContent(image) { Headers = { ContentType = new("image/png") } }, "identity", "presenter.png" },
            { new ByteArrayContent(voice) { Headers = { ContentType = new("audio/wav") } }, "voice", "voice.wav" }
        };

        var createResponse = await client.PostAsync(
            $"/api/v2/projects/{projectId}/digital-presenters/",
            form);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var presenter = await createResponse.Content.ReadFromJsonAsync<DigitalPresenterView>();
        Assert.NotNull(presenter);
        Assert.Equal("法务讲解员", presenter.Name);
        Assert.Equal(image, await client.GetByteArrayAsync(
            $"/api/v2/projects/{projectId}/digital-presenters/media/{presenter.IdentityImageAssetId}"));

        var listed = await client.GetFromJsonAsync<DigitalPresenterView[]>(
            $"/api/v2/projects/{projectId}/digital-presenters/");
        Assert.Single(listed!);

        var episodeResponse = await client.PostAsJsonAsync(
            $"/api/v2/projects/{projectId}/digital-presenters/{presenter.Id}/episodes",
            new
            {
                title = "合同签署提醒",
                dialogue = "签合同之前，请先核对签约主体和授权范围。重要付款条件必须写清楚，不要只依赖口头承诺。"
            });

        Assert.Equal(HttpStatusCode.Created, episodeResponse.StatusCode);
        var episode = await episodeResponse.Content.ReadFromJsonAsync<DigitalPresenterEpisodeView>();
        Assert.NotNull(episode);
        Assert.Equal("合同签署提醒", episode.Title);
        Assert.True(episode.Shots.Count >= 2);
        Assert.All(episode.Shots, shot => Assert.InRange(shot.DurationSeconds, 4, 15));

        var reloaded = await client.GetFromJsonAsync<DigitalPresenterView[]>(
            $"/api/v2/projects/{projectId}/digital-presenters/");
        var reloadedPresenter = Assert.Single(reloaded!);
        var reloadedEpisode = Assert.Single(reloadedPresenter.Episodes);
        Assert.Equal(episode.Shots.Count, reloadedEpisode.Shots.Count);
    }

    [Fact]
    public async Task Video_prompt_uses_h3_dialogue_format_once()
    {
        using var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        var image = new byte[] { 0x89, 0x50, 0x4e, 0x47 };
        var voice = "RIFF0000WAVE"u8.ToArray();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("H3 测试人物"), "name" },
            { new ByteArrayContent(image) { Headers = { ContentType = new("image/png") } }, "identity", "presenter.png" },
            { new ByteArrayContent(voice) { Headers = { ContentType = new("audio/wav") } }, "voice", "voice.wav" }
        };
        var presenter = await (await client.PostAsync($"/api/v2/projects/{projectId}/digital-presenters/", form)).Content.ReadFromJsonAsync<DigitalPresenterView>();
        Assert.NotNull(presenter);
        var episode = await (await client.PostAsJsonAsync($"/api/v2/projects/{projectId}/digital-presenters/{presenter.Id}/episodes", new { title = "测试", dialogue = "你好，世界。" })).Content.ReadFromJsonAsync<DigitalPresenterEpisodeView>();
        var prompt = await client.PostAsync($"/api/v2/projects/{projectId}/digital-presenters/{presenter.Id}/episodes/{episode!.Id}/shots/{episode.Shots[0].Id}/video-prompt", null);
        var body = await prompt.Content.ReadFromJsonAsync<JsonElement>();
        var text = body.GetProperty("videoPrompt").GetString()!;
        Assert.Single(Regex.Matches(text, "<d>\\[Chinese\\] 你好，世界。</d>"));
        Assert.Contains("(S1)", text);
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v2/projects",
            new { name = "数字人口播", description = "数字人剧集测试项目" });
        response.EnsureSuccessStatusCode();
        var project = await response.Content.ReadFromJsonAsync<ProjectView>();
        Assert.NotNull(project);
        return project.Id;
    }
}