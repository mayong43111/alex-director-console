using AlexDirectorConsole.V2.Api.Application.Cqrs;
using AlexDirectorConsole.V2.Api.Features.Projects.Queries;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.Projects.Sources;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record VisualAssetKindResult(
    string Kind,
    int ActiveTotal,
    IReadOnlyList<string> Names);

public sealed record BuildVisualAssetsResult(
    int Created,
    int Skipped,
    int ActiveTotal,
    IReadOnlyList<VisualAssetKindResult> Kinds);

public sealed record VisualImageGenerationStepResult(
    int Generated,
    int AlreadyPresent,
    int Failed,
    int Remaining,
    IReadOnlyList<string> Errors);

public interface IVisualAssetProductionToolService
{
    Task<BuildVisualAssetsResult> BuildFromCurrentScriptsAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<BatchVisualReferenceResult> GenerateMissingPromptsAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken);

    Task<VisualImageGenerationStepResult> GenerateMissingImagesAsync(
        Guid projectId,
        string kind,
        int maxItems,
        CancellationToken cancellationToken);
}

public sealed class VisualAssetProductionToolService(
    ICommandDispatcher commandDispatcher,
    IQueryDispatcher queryDispatcher,
    IProjectSettingsToolService projectSettingsToolService,
    IVisualReferenceService visualReferenceService) : IVisualAssetProductionToolService
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async Task<BuildVisualAssetsResult> BuildFromCurrentScriptsAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var episodes = await queryDispatcher.QueryAsync(
                new ListProductionEpisodesQuery(projectId),
                cancellationToken);
            if (episodes.Count == 0)
            {
                throw new InvalidOperationException("项目还没有正式剧本。");
            }

            var packages = new List<ProductionScriptPackageView>();
            foreach (var episode in episodes)
            {
                var package = await queryDispatcher.QueryAsync(
                    new GetProductionScriptPackageQuery(projectId, episode.Id),
                    cancellationToken);
                if (package is not null) packages.Add(package);
            }
            if (packages.Count == 0)
            {
                throw new InvalidOperationException("项目还没有可读取的正式剧本包。");
            }

            var settings = await projectSettingsToolService.ReadAsync(projectId, cancellationToken);
            var existing = await queryDispatcher.QueryAsync(
                new ListVisualAssetsQuery(projectId, null),
                cancellationToken);
            var existingKeys = existing
                .Select(asset => $"{asset.Kind}\n{asset.Name}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requests = BuildRequests(packages, settings)
                .Where(request => existingKeys.Add($"{request.Kind}\n{request.Name}"))
                .ToArray();

            foreach (var request in requests)
            {
                var result = await commandDispatcher.SendAsync(
                    new SaveVisualAssetCommand(projectId, null, request),
                    cancellationToken);
                if (result.Status != SaveVisualAssetStatus.Success)
                {
                    throw new InvalidOperationException(string.Join(
                        " ",
                        result.Errors.SelectMany(error => error.Value)));
                }
            }

            var assets = await queryDispatcher.QueryAsync(
                new ListVisualAssetsQuery(projectId, null),
                cancellationToken);
            return new BuildVisualAssetsResult(
                requests.Length,
                existing.Count,
                assets.Count,
                assets
                    .GroupBy(asset => asset.Kind)
                    .OrderBy(group => group.Key)
                    .Select(group => new VisualAssetKindResult(
                        group.Key,
                        group.Count(),
                        group.Select(asset => asset.Name).ToArray()))
                    .ToArray());
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<BatchVisualReferenceResult> GenerateMissingPromptsAsync(
        Guid projectId,
        string kind,
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            return await visualReferenceService.GenerateMissingPromptsAsync(
                projectId,
                kind,
                cancellationToken);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<VisualImageGenerationStepResult> GenerateMissingImagesAsync(
        Guid projectId,
        string kind,
        int maxItems,
        CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            kind = kind.Trim().ToLowerInvariant();
            if (kind is not ("character" or "scene" or "prop"))
            {
                throw new InvalidOperationException("批量生成仅支持人物、场景或道具资产。");
            }
            maxItems = Math.Clamp(maxItems, 1, 3);
            var assets = await queryDispatcher.QueryAsync(
                new ListVisualAssetsQuery(projectId, kind),
                cancellationToken);
            var missing = assets.Where(asset => asset.ReferenceImage is null).ToArray();
            var generated = 0;
            var errors = new List<string>();
            foreach (var asset in missing.Take(maxItems))
            {
                try
                {
                    await visualReferenceService.GenerateImageAsync(
                        projectId,
                        asset.ResourceId,
                        cancellationToken);
                    generated++;
                }
                catch (Exception error) when (error is InvalidOperationException or HttpRequestException)
                {
                    errors.Add($"{asset.Name}: {error.Message}");
                }
                catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
                {
                    errors.Add($"{asset.Name}: 图片生成请求超时。{error.Message}");
                }
            }
            return new VisualImageGenerationStepResult(
                generated,
                assets.Count - missing.Length,
                errors.Count,
                missing.Length - generated,
                errors);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private static IEnumerable<SaveVisualAssetRequest> BuildRequests(
        IReadOnlyList<ProductionScriptPackageView> packages,
        ProjectSettingsView settings)
    {
        var scenes = packages.SelectMany(package => package.Episode.Scenes.Select(scene => new
        {
            Package = package,
            Scene = scene,
            Reference = $"E{package.EpisodeNumber:D2} · S{scene.SceneNumber:D2}"
        })).ToArray();

        foreach (var group in scenes
            .SelectMany(item => item.Scene.Characters
                .Concat(item.Scene.Dialogues.Select(dialogue => dialogue.Character))
                .Select(name => new { Name = name.Trim(), item.Package, item.Reference }))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            yield return new SaveVisualAssetRequest(
                "character",
                first.Name,
                "正式剧本中的固定出镜角色。",
                first.Name.Equals("主持人", StringComparison.OrdinalIgnoreCase)
                    ? settings.CharacterDesign
                    : string.Empty,
                [],
                [],
                group.Select(item => item.Reference).Distinct().ToArray(),
                first.Package.AssetId);
        }

        foreach (var group in scenes.GroupBy(
            item => item.Scene.Heading.Trim(),
            StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            yield return new SaveVisualAssetRequest(
                "scene",
                first.Scene.Heading.Trim(),
                first.Scene.Summary,
                first.Scene.Action,
                [],
                [],
                group.Select(item => item.Reference).Distinct().ToArray(),
                first.Package.AssetId);
        }

        foreach (var group in scenes
            .SelectMany(item => item.Scene.Props.Select(name => new
            {
                Name = name.Trim(),
                item.Package,
                item.Reference
            }))
            .Where(item => item.Name.Length > 0)
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            yield return new SaveVisualAssetRequest(
                "prop",
                first.Name,
                "正式剧本中明确出现的视觉道具。",
                first.Name,
                [],
                [],
                group.Select(item => item.Reference).Distinct().ToArray(),
                first.Package.AssetId);
        }
    }
}