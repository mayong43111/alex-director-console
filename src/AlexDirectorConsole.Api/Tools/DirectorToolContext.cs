using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;

namespace AlexDirectorConsole.Api.Tools;

public sealed class DirectorToolContext : IDisposable
{
    public required Guid ProjectId { get; init; }
    public required string Content { get; init; }
    public required string? RequestedModel { get; init; }
    public required string ImageSize { get; init; }
    public required string ImageDeployment { get; init; }
    public required Asset? CurrentAsset { get; init; }
    public required string? CurrentAssetContent { get; init; }
    public required Func<object, CancellationToken, ValueTask> EventWriter { get; init; }

    public JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public SemaphoreSlim ResourceLock { get; } = new(1, 1);
    public List<Asset> RevisedAssets { get; } = [];
    public List<VideoPromptRecord> VideoPrompts { get; } = [];
    public SkillExecutionResult? Execution { get; set; }
    public Asset? UpdatedAsset { get; set; }

    public ValueTask WriteEventAsync(object value, CancellationToken cancellationToken) =>
        EventWriter(value, cancellationToken);

    public void Dispose() => ResourceLock.Dispose();
}

public sealed record VideoPromptRecord(string ResourceName, string Prompt, string Workflow, int Width, int Height, int FrameCount, int Fps);
