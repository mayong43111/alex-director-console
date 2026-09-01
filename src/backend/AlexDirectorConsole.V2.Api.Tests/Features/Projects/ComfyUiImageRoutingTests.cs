using System.Text.Json.Nodes;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Storyboard;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;
using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace AlexDirectorConsole.V2.Api.Tests.Features.Projects;

public sealed class ComfyUiImageRoutingTests
{
    [Theory]
    [InlineData(854, 480, "16:9", "864x480")]
    [InlineData(1920, 1080, "16:9", "1920x1088")]
    public void ComfyUi_model_size_rounds_each_dimension_to_sixteen(
        int width,
        int height,
        string aspectRatio,
        string expected)
    {
        var result = ProjectImageOutputProcessor.ModelSizeFor(
            width,
            height,
            aspectRatio,
            "comfyui");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task Image_client_rejects_more_than_eight_references_before_submission()
    {
        var client = new ComfyUiImageClient(new RejectingHttpClientFactory());
        var workflow = await new PackagedComfyUiImageWorkflowProvider().ReadImageEditAsync(
            ComfyUiConfigurationView.Flux2DevImageEditWorkflow,
            CancellationToken.None);
        var references = Enumerable.Range(0, 9)
            .Select(_ => new ComfyUiImageReference([1], "image/png"))
            .ToArray();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(
            new("http://127.0.0.1:8188", workflow, "prompt", 864, 480, references),
            CancellationToken.None));

        Assert.Contains("最多支持 8 张参考图", error.Message);
    }

    [Fact]
    public async Task Flux_workflow_uses_requested_dimensions_and_kv_cache()
    {
        var workflowJson = await new PackagedComfyUiImageWorkflowProvider()
            .ReadImageEditAsync(ComfyUiConfigurationView.Flux2DevImageEditWorkflow, CancellationToken.None);
        var workflow = JsonNode.Parse(workflowJson)!.AsObject();

        Assert.Equal("FluxKVCache", workflow["5"]!["class_type"]!.GetValue<string>());
        Assert.Equal("EmptyFlux2LatentImage", workflow["50"]!["class_type"]!.GetValue<string>());
        Assert.Equal("{{WIDTH}}", workflow["50"]!["inputs"]!["width"]!.GetValue<string>());
        Assert.Equal("{{HEIGHT}}", workflow["50"]!["inputs"]!["height"]!.GetValue<string>());
    }

    [Fact]
    public async Task Krea_workflow_allows_text_to_image_without_references()
    {
        var workflowJson = await new PackagedComfyUiImageWorkflowProvider()
            .ReadTextToImageAsync(CancellationToken.None);
        var client = new ComfyUiImageClient(new RejectingSubmissionHttpClientFactory());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync(
            new("http://127.0.0.1:8188", workflowJson, "山间车站", 1024, 1024, []),
            CancellationToken.None));

        Assert.Contains("ComfyUI 拒绝图片 workflow", error.Message);
        Assert.DoesNotContain("至少需要一张参考图", error.Message);
    }

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
    public async Task Shot_frame_generation_uses_configured_qwen_image_edit_workflow()
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
        Assert.Equal(ComfyUiConfigurationView.QwenImageEditWorkflow, imageClient.Request?.WorkflowJson);
        Assert.Equal(1024, imageClient.Request?.Width);
        Assert.Equal(1536, imageClient.Request?.Height);
        Assert.Single(imageClient.Request?.References ?? []);
    }

    [Fact]
    public async Task Shot_frame_generation_preserves_all_flux_references()
    {
        await using var connection = await CreateConnectionAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.ComfyUiConfigurations.Single().ImageEditWorkflow = ComfyUiConfigurationView.Flux2DevImageEditWorkflow;
        await dbContext.SaveChangesAsync();
        var imageClient = new RecordingImageClient();
        var generator = new AzureFoundryShotFrameGenerator(
            new HttpClient(),
            dbContext,
            null!,
            imageClient,
            new TestWorkflowProvider());
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var references = new[]
        {
            Reference("scene", "街口", png),
            Reference("character", "刘备", png),
            Reference("character", "关羽", png),
            Reference("character", "张飞", png)
        };

        var result = await generator.GenerateAsync(
            "保持场景和三人一致",
            "864x480",
            references,
            CancellationToken.None);

        var submitted = Assert.IsType<ComfyUiImageRequest>(imageClient.Request);
        Assert.Equal("FLUX.2 dev", result.Deployment);
        Assert.Equal(ComfyUiConfigurationView.Flux2DevImageEditWorkflow, submitted.WorkflowJson);
        Assert.Equal(4, submitted.References.Count);
        Assert.Equal(png, submitted.References[0].Bytes);
        Assert.Equal(png, submitted.References[3].Bytes);
    }

    private static ShotFrameReference Reference(string type, string name, byte[] bytes) => new(
        bytes,
        "image/png",
        $"{name}.png",
        type,
        name,
        Guid.NewGuid(),
        Guid.NewGuid(),
        1);

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

    private sealed class RejectingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("容量校验后不应创建 HTTP 客户端。");
    }

    private sealed class RejectingSubmissionHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RejectingSubmissionHandler());
    }

    private sealed class RejectingSubmissionHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });
    }

    private sealed class TestWorkflowProvider : IComfyUiImageWorkflowProvider
    {
        public Task<string> ReadTextToImageAsync(CancellationToken cancellationToken) =>
            Task.FromResult("text-to-image-workflow");

        public Task<string> ReadImageEditAsync(string workflow, CancellationToken cancellationToken) =>
            Task.FromResult(workflow);
    }
}