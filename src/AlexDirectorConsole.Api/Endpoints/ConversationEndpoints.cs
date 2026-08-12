using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Endpoints;

public static class ConversationEndpoints
{
    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/projects/{projectId:guid}/messages",
            async (Guid projectId, AppDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var messages = await dbContext.ConversationMessages
                    .AsNoTracking()
                    .Where(message => message.ProjectId == projectId)
                    .OrderBy(message => message.CreatedAtUtc)
                    .ToListAsync(cancellationToken);

                var generatedAssetIds = messages
                    .SelectMany(ConversationMessageResponse.GetGeneratedAssetIds)
                    .Distinct()
                    .ToArray();
                var generatedAssetsById = generatedAssetIds.Length == 0
                    ? new Dictionary<Guid, Asset>()
                    : await dbContext.Assets
                        .AsNoTracking()
                        .Where(asset => asset.ProjectId == projectId && generatedAssetIds.Contains(asset.Id))
                        .ToDictionaryAsync(asset => asset.Id, cancellationToken);
                var legacyGeneratedImages = await dbContext.Assets
                    .AsNoTracking()
                    .Where(asset =>
                        asset.ProjectId == projectId
                        && asset.ContentType.StartsWith("image/"))
                    .ToListAsync(cancellationToken);

                var responseMessages = messages.Select(message =>
                {
                    var explicitAssets = ConversationMessageResponse.GetGeneratedAssetIds(message)
                        .Where(generatedAssetsById.ContainsKey)
                        .Select(assetId => AssetResponse.FromAsset(generatedAssetsById[assetId]))
                        .ToArray();
                    var generatedAssets = explicitAssets.Length > 0 || message.Role != "assistant"
                        ? explicitAssets
                        : legacyGeneratedImages
                            .Where(asset => message.Content.Contains(
                                asset.Name,
                                StringComparison.OrdinalIgnoreCase))
                            .Select(AssetResponse.FromAsset)
                            .ToArray();

                    return ConversationMessageResponse.FromMessage(message, generatedAssets);
                }).ToArray();

                return Results.Ok(responseMessages);
            })
            .WithName("GetProjectMessages")
            .WithOpenApi();

        app.MapDelete(
            "/api/projects/{projectId:guid}/messages/{messageId:guid}/following",
            async (
                Guid projectId,
                Guid messageId,
                AppDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                var targetMessage = await dbContext.ConversationMessages
                    .AsNoTracking()
                    .SingleOrDefaultAsync(message =>
                        message.ProjectId == projectId && message.Id == messageId,
                        cancellationToken);
                if (targetMessage is null)
                {
                    return Results.NotFound();
                }

                if (targetMessage.Role != "user")
                {
                    return Results.BadRequest(new { error = "Only user messages can be retried." });
                }

                var messagesToDelete = await dbContext.ConversationMessages
                    .Where(message =>
                        message.ProjectId == projectId
                        && (message.Id == messageId || message.CreatedAtUtc > targetMessage.CreatedAtUtc))
                    .ToListAsync(cancellationToken);
                dbContext.ConversationMessages.RemoveRange(messagesToDelete);
                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.NoContent();
            })
            .WithName("DeleteProjectMessagesFrom")
            .WithOpenApi();

        app.MapPost(
            "/api/projects/{projectId:guid}/messages",
            async (
                Guid projectId,
                SendMessageRequest request,
                AppDbContext dbContext,
                IDirectorAgent directorAgent,
                ILogger<ConversationEndpointsLog> logger,
                CancellationToken cancellationToken) =>
            {
                var content = request.Message?.Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return Results.BadRequest(new { error = "Message is required." });
                }

                if (content.Length > 20000)
                {
                    return Results.BadRequest(new { error = "Message cannot exceed 20,000 characters." });
                }

                if (!string.IsNullOrWhiteSpace(request.Model) && request.Model.Trim().Length > 100)
                {
                    return Results.BadRequest(new { error = "Model deployment name cannot exceed 100 characters." });
                }

                if (!directorAgent.IsConfigured)
                {
                    return Results.Problem(
                        title: "Azure AI Foundry is not configured",
                        detail: "Set AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, and AZURE_OPENAI_DEPLOYMENT in .env.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var history = await dbContext.ConversationMessages
                    .AsNoTracking()
                    .Where(message => message.ProjectId == projectId)
                    .OrderByDescending(message => message.CreatedAtUtc)
                    .Take(40)
                    .ToListAsync(cancellationToken);
                history.Reverse();

                DirectorAgentReply reply;
                try
                {
                    reply = await directorAgent.ReplyAsync(
                        history,
                        content,
                        request.Model,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Azure AI Foundry conversation failed for project {ProjectId}", projectId);
                    return Results.Problem(
                        title: "Azure AI Foundry request failed",
                        detail: "The execution assistant could not produce a response.",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                var now = DateTime.UtcNow;
                var userMessage = new ConversationMessage
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Role = "user",
                    Content = content,
                    Model = reply.Deployment,
                    CreatedAtUtc = now
                };
                var assistantMessage = new ConversationMessage
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    Role = "assistant",
                    Content = reply.Text,
                    Model = reply.Deployment,
                    CreatedAtUtc = now.AddTicks(1)
                };

                dbContext.ConversationMessages.AddRange(userMessage, assistantMessage);
                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Ok(new SendMessageResponse(
                    ConversationMessageResponse.FromMessage(userMessage),
                    ConversationMessageResponse.FromMessage(assistantMessage)));
            })
            .WithName("SendProjectMessage")
            .WithOpenApi();

        return app;
    }

    private sealed class ConversationEndpointsLog;
}