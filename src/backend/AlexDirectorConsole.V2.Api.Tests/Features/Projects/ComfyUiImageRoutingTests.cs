using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ComfyUiImageRoutingTests
{
    [Fact]
    public async Task Cover_generation_uses_krea_text_to_image_workflow()
    {
        await using var connection = await CreateConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var imageClient = new RecordingImageClient();
        var generator = new AzureFoundryProjectCoverGenerator(
            new HttpClient(),
            dbContext,
            null!,
            imageClient,
            new TestWorkflowProvider());

        var result = await generator.GenerateAsync("山间车站", "1536x1024", CancellationToken.None);

        Assert.Equal("Krea 2 Turbo", result.Deployment);
        Assert.Equal("medium", result.Quality);
        Assert.Equal("text-to-image-workflow", imageClient.Request?.WorkflowJson);
        Assert.Equal(1536, imageClient.Request?.Width);
        Assert.Equal(1024, imageClient.Request?.Height);
        Assert.Empty(imageClient.Request?.References ?? []);
    }

    [Fact]
    public async Task Shot_frame_generation_uses_qwen_image_edit_workflow()
    {
        await using var connection = await CreateConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var imageClient = new RecordingImageClient();
        var generator = new AzureFoundryShotFrameGenerator(
            new HttpClient(),
            dbContext,
            null!,
            imageClient,
            new TestWorkflowProvider());
        var reference = new ShotFrameReference(
            [1, 2, 3],
            "image/png",
            "reference.png",
            "character",
            "主角",
            Guid.NewGuid(),
            Guid.NewGuid(),
            1);

        var result = await generator.GenerateAsync(
            "保持角色一致",
            "1024x1536",
            [reference],
            CancellationToken.None);

        Assert.Equal("Qwen Image Edit 2511", result.Deployment);
        Assert.Equal("medium", result.Quality);
        Assert.Equal("image-edit-workflow", imageClient.Request?.WorkflowJson);
        Assert.Equal(1024, imageClient.Request?.Width);
        Assert.Equal(1536, imageClient.Request?.Height);
        Assert.Single(imageClient.Request?.References ?? []);
    }

    private static async Task<SqliteConnection> CreateConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<V2DbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<V2DbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new V2DbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.FoundryConfigurations.Add(new FoundryConfiguration
        {
            ImageProvider = "comfyui",
            ImageQuality = "medium",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        dbContext.ComfyUiConfigurations.Add(new ComfyUiConfiguration
        {
            BaseUrl = "http://127.0.0.1:8188",
            IsEnabled = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        return dbContext;
    }

    private sealed class RecordingImageClient : IComfyUiImageClient
    {
        public ComfyUiImageRequest? Request { get; private set; }

        public Task<ComfyUiGeneratedImage> GenerateAsync(
            ComfyUiImageRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ComfyUiGeneratedImage([137, 80, 78, 71], "image/png"));
        }
    }

    private sealed class TestWorkflowProvider : IComfyUiImageWorkflowProvider
    {
        public Task<string> ReadTextToImageAsync(CancellationToken cancellationToken) =>
            Task.FromResult("text-to-image-workflow");

        public Task<string> ReadImageEditAsync(CancellationToken cancellationToken) =>
            Task.FromResult("image-edit-workflow");
    }
}