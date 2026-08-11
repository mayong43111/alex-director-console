using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Storage;

namespace AlexDirectorConsole.Api.Tools;

public sealed class DirectorToolContext : IDisposable
{
    public required Guid ProjectId { get; init; }
    public required string Content { get; init; }
    public required string? RequestedModel { get; init; }
    public required Asset? CurrentAsset { get; init; }
    public required string? CurrentAssetContent { get; init; }
    public required AppDbContext DbContext { get; init; }
    public required HttpResponse Response { get; init; }
    public required IBlobStorage BlobStorage { get; init; }
    public required IAzureFoundryImageGenerator ImageGenerator { get; init; }
    public required IAgentSkillExecutor SkillExecutor { get; init; }

    public JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public SemaphoreSlim ResourceLock { get; } = new(1, 1);
    public List<Asset> RevisedAssets { get; } = [];
    public SkillExecutionResult? Execution { get; set; }
    public Asset? UpdatedAsset { get; set; }

    public async ValueTask WriteEventAsync(object value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Response.WriteAsync(json + "\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    public void Dispose() => ResourceLock.Dispose();
}
