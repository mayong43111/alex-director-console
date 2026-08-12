using System.Text;
using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Storage;
using AlexDirectorConsole.Api.Tools;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Conversations;

public sealed class DirectorSessionService(
    AppDbContext dbContext,
    IDirectorAgent directorAgent,
    IAzureFoundryImageGenerator imageGenerator,
    IProjectSkillCatalog skillCatalog,
    IDirectorToolRegistry toolRegistry,
    IAssetReader assetContentReader,
    ILogger<DirectorSessionService> logger) : IDirectorSessionService
{
    public async Task ExecuteAsync(
        Guid projectId,
        SendMessageRequest request,
        IDirectorSessionStream stream,
        CancellationToken cancellationToken)
    {
        var content = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 20000)
        {
            await stream.RejectAsync(400, "Message is required and cannot exceed 20,000 characters.", cancellationToken);
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.ImageModel) && request.ImageModel.Trim().Length > 100)
        {
            await stream.RejectAsync(400, "Image model deployment name cannot exceed 100 characters.", cancellationToken);
            return;
        }

        if (!directorAgent.IsConfigured)
        {
            await stream.RejectAsync(503, "Azure AI Foundry is not configured.", cancellationToken);
            return;
        }

        var (currentAsset, currentAssetContent) = await LoadCurrentAssetAsync(
            projectId,
            request.AssetId,
            cancellationToken);
        if (request.AssetId is not null && currentAsset is null)
        {
            await stream.RejectAsync(400, "The current asset does not belong to this project.", cancellationToken);
            return;
        }

        var history = await LoadHistoryAsync(projectId, cancellationToken);
        var recentGeneratedImages = await GetRecentGeneratedImagesAsync(
            projectId,
            history,
            cancellationToken);
        var availableSkillPaths = await GetAvailableSkillPathsAsync(cancellationToken);

        await stream.StartAsync(cancellationToken);
        await stream.WriteAsync(new
        {
            type = "message.accepted",
            message = "已接收导演令"
        }, cancellationToken);

        try
        {
            await ExecuteSessionAsync(
                projectId,
                request,
                content,
                currentAsset,
                currentAssetContent,
                history,
                recentGeneratedImages,
                availableSkillPaths,
                stream,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Streaming Agent request failed for project {ProjectId}", projectId);
            await stream.WriteAsync(new
            {
                type = "error",
                message = "执行副导演未能完成本次请求。",
                detail = exception.Message
            }, CancellationToken.None);
        }
    }

    private async Task ExecuteSessionAsync(
        Guid projectId,
        SendMessageRequest request,
        string content,
        Asset? currentAsset,
        string? currentAssetContent,
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<Asset> recentGeneratedImages,
        IReadOnlyList<string> availableSkillPaths,
        IDirectorSessionStream stream,
        CancellationToken cancellationToken)
    {
        var deployment = string.IsNullOrWhiteSpace(request.Model)
            ? directorAgent.Deployment
            : request.Model.Trim();
        var replyBuilder = new StringBuilder();
        using var toolContext = CreateToolContext(
            projectId,
            request,
            content,
            currentAsset,
            currentAssetContent,
            stream);
        var tools = toolRegistry.CreateTools(toolContext).ToList();
        var agentContext = DirectorSessionPromptBuilder.BuildAgentContext(
            request,
            toolContext,
            currentAsset,
            currentAssetContent,
            recentGeneratedImages);

        await stream.WriteAsync(new
        {
            type = "process",
            stage = "context.current-resource",
            message = currentAsset is null
                ? "当前未选择资源"
                : $"已载入当前资源：{currentAsset.Name}"
        }, cancellationToken);
        await stream.WriteAsync(new
        {
            type = "process",
            stage = "agent.started",
            message = $"正在调用 {deployment}，由 Agent 自主选择技能与工具"
        }, cancellationToken);

        await foreach (var delta in directorAgent.StreamReplyWithToolsAsync(
            history,
            content,
            agentContext,
            request.Model,
            tools,
            availableSkillPaths,
            cancellationToken))
        {
            replyBuilder.Append(delta);
            await stream.WriteAsync(new { type = "assistant.delta", delta }, cancellationToken);
        }

        await DirectorSessionPromptBuilder.AppendPromptRecordsAsync(
            replyBuilder,
            toolContext,
            stream,
            cancellationToken);

        var execution = toolContext.Execution;
        if (execution is not null)
        {
            deployment = execution.Run.Model;
        }

        var revisedAssets = toolContext.RevisedAssets;
        var generatedAssets = execution is not null
            ? execution.GeneratedAssets
                .Concat(revisedAssets)
                .DistinctBy(asset => asset.Id)
                .ToArray()
            : revisedAssets.ToArray();
        var updatedAsset = toolContext.UpdatedAsset;

        var now = DateTime.UtcNow;
        var userMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Role = "user",
            Content = content,
            Model = deployment,
            CreatedAtUtc = now
        };
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Role = "assistant",
            Content = replyBuilder.ToString(),
            Model = deployment,
            GeneratedAssetIdsJson = generatedAssets.Length == 0
                ? null
                : JsonSerializer.Serialize(generatedAssets.Select(asset => asset.Id)),
            CreatedAtUtc = now.AddTicks(1)
        };
        dbContext.ConversationMessages.AddRange(userMessage, assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        await stream.WriteAsync(new
        {
            type = "completed",
            userMessage = ConversationMessageResponse.FromMessage(userMessage),
            assistantMessage = ConversationMessageResponse.FromMessage(
                assistantMessage,
                generatedAssets.Select(AssetResponse.FromAsset).ToArray()),
            skillRun = execution is null ? null : SkillRunResponse.FromRun(execution.Run),
            outputAsset = execution?.OutputAsset is null
                ? null
                : AssetResponse.FromAsset(execution.OutputAsset),
            generatedAssets = generatedAssets.Select(AssetResponse.FromAsset),
            updatedAsset = updatedAsset is null ? null : AssetResponse.FromAsset(updatedAsset)
        }, cancellationToken);
    }

    private DirectorToolContext CreateToolContext(
        Guid projectId,
        SendMessageRequest request,
        string content,
        Asset? currentAsset,
        string? currentAssetContent,
        IDirectorSessionStream stream) => new()
    {
        ProjectId = projectId,
        Content = content,
        RequestedModel = request.Model,
        ImageSize = request.ImageSize is "1536x1024" or "1024x1536"
            ? request.ImageSize
            : "1024x1024",
        ImageDeployment = string.IsNullOrWhiteSpace(request.ImageModel)
            ? imageGenerator.Deployment
            : request.ImageModel.Trim(),
        CurrentAsset = currentAsset,
        CurrentAssetContent = currentAssetContent,
        EventWriter = stream.WriteAsync
    };

    private async Task<(Asset? Asset, string? Content)> LoadCurrentAssetAsync(
        Guid projectId,
        Guid? assetId,
        CancellationToken cancellationToken)
    {
        if (assetId is null) return (null, null);

        var asset = await dbContext.Assets.SingleOrDefaultAsync(
            item => item.Id == assetId && item.ProjectId == projectId,
            cancellationToken);
        if (asset is null || !IsTextAsset(asset)) return (asset, null);

        await using var assetStream = await assetContentReader.OpenReadAsync(projectId, asset, cancellationToken);
        if (assetStream is null) return (asset, null);

        using var assetReader = new StreamReader(assetStream, detectEncodingFromByteOrderMarks: true);
        return (asset, await assetReader.ReadToEndAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<ConversationMessage>> LoadHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var history = await dbContext.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ProjectId == projectId)
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(40)
            .ToListAsync(cancellationToken);
        history.Reverse();
        return history;
    }

    private async Task<IReadOnlyList<Asset>> GetRecentGeneratedImagesAsync(
        Guid projectId,
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken)
    {
        var assetIds = history
            .Reverse()
            .Where(message => message.Role == "assistant")
            .SelectMany(ConversationMessageResponse.GetGeneratedAssetIds)
            .Distinct()
            .Take(10)
            .ToArray();
        if (assetIds.Length == 0) return [];

        var assetsById = await dbContext.Assets
            .AsNoTracking()
            .Where(asset =>
                asset.ProjectId == projectId
                && assetIds.Contains(asset.Id)
                && asset.ContentType.StartsWith("image/"))
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);
        return assetIds
            .Where(assetsById.ContainsKey)
            .Select(assetId => assetsById[assetId])
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> GetAvailableSkillPathsAsync(CancellationToken cancellationToken)
    {
        var enabledSkillIdList = await dbContext.SkillDefinitions
            .AsNoTracking()
            .Where(skill => skill.IsEnabled)
            .OrderBy(skill => skill.Name)
            .Select(skill => skill.Id)
            .ToListAsync(cancellationToken);
        var enabledSkillIds = enabledSkillIdList.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return skillCatalog.List()
            .Where(skill => enabledSkillIds.Contains(skill.Name))
            .Select(skill => Path.GetDirectoryName(skill.FilePath)!)
            .ToArray();
    }

    private static bool IsTextAsset(Asset asset) =>
        asset.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || asset.ContentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".md", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(asset.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase);
}
