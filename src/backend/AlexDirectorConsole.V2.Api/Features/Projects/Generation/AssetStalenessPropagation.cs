using AlexDirectorConsole.V2.Database.Data;
using AlexDirectorConsole.V2.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Generation;

internal static class AssetStalenessPropagation
{
    public static async Task MarkRequiredDependentsStaleAsync(
        V2DbContext dbContext,
        Asset previousAsset,
        Asset replacementAsset,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pending = new Queue<Guid>();
        var visited = new HashSet<Guid> { previousAsset.Id };
        pending.Enqueue(previousAsset.Id);
        var reason = $"上游资源 {previousAsset.ResourceId} 已从 v{previousAsset.Version} 更新为 v{replacementAsset.Version}。";

        while (pending.TryDequeue(out var sourceAssetId))
        {
            var consumerAssetIds = await dbContext.AssetDependencies.AsNoTracking()
                .Where(item => item.ProjectId == previousAsset.ProjectId
                    && item.SourceAssetId == sourceAssetId
                    && item.IsRequired)
                .Select(item => item.ConsumerAssetId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (consumerAssetIds.Length == 0) continue;

            var currentStates = await dbContext.ResourceStates
                .Where(item => item.ProjectId == previousAsset.ProjectId
                    && consumerAssetIds.Contains(item.CurrentAssetId))
                .ToListAsync(cancellationToken);
            foreach (var consumerAssetId in consumerAssetIds)
            {
                if (visited.Add(consumerAssetId))
                {
                    pending.Enqueue(consumerAssetId);
                }
            }
            foreach (var state in currentStates)
            {
                state.IsStale = true;
                state.StaleReason = reason;
                state.StaleSinceUtc ??= now;
                state.UpdatedAtUtc = now;
            }
        }
    }
}