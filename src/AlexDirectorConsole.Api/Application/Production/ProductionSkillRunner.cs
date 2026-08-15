using System.Text.Json;
using AlexDirectorConsole.Api.Application.Assets;
using AlexDirectorConsole.Api.Data;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Services;
using AlexDirectorConsole.Api.Tools;
using Microsoft.EntityFrameworkCore;

namespace AlexDirectorConsole.Api.Application.Production;

public interface IProductionSkillRunner
{
    Task<Guid?> FindExistingOutputAsync(
        ProductionRun run,
        ProductionRunItem item,
        CancellationToken cancellationToken);

    Task<Guid> ExecuteShotStageAsync(
        ProductionRun run,
        ProductionRunItem item,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Guid>> ExecuteVideoBatchAsync(
        ProductionRun run,
        IReadOnlyList<ProductionRunItem> items,
        CancellationToken cancellationToken);

    Task<Guid> AssembleAsync(ProductionRun run, CancellationToken cancellationToken);
}

public sealed class ProductionSkillRunner(
    AppDbContext dbContext,
    IAssetReader assetReader,
    IDirectorAgent directorAgent,
    IProjectSkillCatalog skillCatalog,
    IDirectorToolRegistry toolRegistry,
    IAzureFoundryImageGenerator imageGenerator) : IProductionSkillRunner
{
    private static readonly IReadOnlyDictionary<string, StageDefinition> Stages =
        new Dictionary<string, StageDefinition>(StringComparer.Ordinal)
        {
            ["frames"] = new(
                ["shot-first-frame", "image-generation-prompt"],
                ["list_project_resources", "list_shot_first_frame_status", "query_storyboard",
                    "read_project_resource_contents", "read_project_resources", "inspect_visual_references",
                    "merge_reference_images", "generate_image", "generate_image_from_references", "bind_shot_asset"],
                "first-frame",
                "image/",
                "为当前这一个 shot 生成并绑定首帧。必须加载 shot-first-frame 和 image-generation-prompt。优先使用全部明确可用参考图；缺失参考图已获导演授权按最新文字设定继续，不得为缺失项停下来询问。只处理当前 shot，成功绑定 first-frame 后结束。"),
            ["videos"] = new(
                ["minimax-h3-video", "minimax-h3-video-prompt"],
                ["list_project_resources", "query_storyboard", "read_project_resource_contents",
                    "inspect_remote_comfyui", "manage_remote_comfyui", "generate_comfyui_video", "bind_shot_asset"],
                "video",
                "video/",
                "为当前这一个 shot 使用已绑定首帧生成 MiniMax H3 视频并绑定 video。必须加载 minimax-h3-video 和 minimax-h3-video-prompt。没有尾帧时复用首帧。只处理当前 shot，成功绑定后结束。"),
            ["narration"] = new(
                ["voice-over"],
                ["list_project_resources", "query_storyboard", "read_project_resource_contents",
                    "generate_speech", "bind_shot_asset"],
                "other",
                "audio/",
                "为当前这一个 shot 生成中文产品介绍配音，并立即以 other 绑定到当前 shot。必须加载 voice-over。正文含完整旁白或对白时使用该文本；只有‘旁白第几场前半’一类占位引用时，根据产品名、导演令、镜头画面和视觉主旨写一句适合镜头时长、逐字可核对的产品介绍文案，再生成语音。不得朗读画面说明；只处理当前 shot，成功绑定后结束。")
        };

    public async Task<Guid?> FindExistingOutputAsync(
        ProductionRun run,
        ProductionRunItem item,
        CancellationToken cancellationToken)
    {
        if (!Stages.TryGetValue(item.Stage, out var stage))
        {
            throw new InvalidOperationException($"未知生产阶段：{item.Stage}。");
        }
        var output = await FindBoundAssetAsync(item, stage, null, cancellationToken);
        if (output is null || item.Stage != "videos")
            return output?.Id;

        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            project => project.Id == item.ProjectId,
            cancellationToken);
        var canvas = GetRunCanvas(run, project.PreviewResolution);
        return HasVideoCanvas(output, canvas) ? output.Id : null;
    }

    public async Task<Guid> ExecuteShotStageAsync(
        ProductionRun run,
        ProductionRunItem item,
        CancellationToken cancellationToken)
    {
        if (!Stages.TryGetValue(item.Stage, out var stage))
        {
            throw new InvalidOperationException($"未知生产阶段：{item.Stage}。");
        }
        var existing = await FindBoundAssetAsync(item, stage, null, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var shot = await dbContext.Assets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                asset => asset.Id == item.ShotAssetId
                    && asset.ProjectId == item.ProjectId
                    && asset.Type == "shot",
                cancellationToken)
            ?? throw new InvalidOperationException("生产任务中的 shot 已不存在。");
        await using var stream = await assetReader.OpenReadAsync(item.ProjectId, shot, cancellationToken)
            ?? throw new InvalidOperationException("生产任务中的 shot 正文文件不存在。");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var shotContent = await reader.ReadToEndAsync(cancellationToken);
        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            project => project.Id == item.ProjectId,
            cancellationToken);
        using var context = new DirectorToolContext
        {
            ProjectId = item.ProjectId,
            Content = stage.Instruction,
            RequestedModel = project.LanguageModel,
            ImageSize = project.OutputWidth >= project.OutputHeight ? "1536x1024" : "1024x1536",
            ImageDeployment = string.IsNullOrWhiteSpace(project.ImageModel)
                ? imageGenerator.Deployment
                : project.ImageModel,
            CurrentAsset = shot,
            CurrentAssetContent = shotContent,
            EventWriter = static (_, _) => ValueTask.CompletedTask
        };
        var tools = toolRegistry.CreateTools(
            context,
            stage.ToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();
        var skillPaths = stage.SkillNames.Select(name =>
            Path.GetDirectoryName(skillCatalog.Get(name)?.FilePath
                ?? throw new InvalidOperationException($"生产阶段缺少技能：{name}。"))!)
            .ToArray();
        var resourceContext = $"""
            当前项目 ID：{item.ProjectId}
            当前资源是唯一目标 shot。
            shotAssetId：{shot.Id}
            shotResourceId：{shot.ResourceId}
            shot 名称：{shot.Name}
            shot 完整正文：
            {shotContent}
            """;

        await foreach (var _ in directorAgent.StreamReplyWithToolsAsync(
            [],
            stage.Instruction,
            resourceContext,
            project.LanguageModel,
            tools,
            skillPaths,
            cancellationToken))
        {
        }

        var generatedIds = context.RevisedAssets.Select(asset => asset.Id).ToHashSet();
        var output = await FindBoundAssetAsync(item, stage, generatedIds, cancellationToken)
            ?? throw new InvalidOperationException($"Agent 未为 {item.ShotName} 持久化有效的 {stage.Role} 绑定。");
        return output.Id;
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> ExecuteVideoBatchAsync(
        ProductionRun run,
        IReadOnlyList<ProductionRunItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return new Dictionary<Guid, Guid>();

        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            project => project.Id == run.ProjectId,
            cancellationToken);
        var canvas = GetRunCanvas(run, project.PreviewResolution);
        using var context = new DirectorToolContext
        {
            ProjectId = run.ProjectId,
            Content = "为一句话成片任务一次生成全部待处理镜头视频。",
            RequestedModel = project.LanguageModel,
            ImageSize = project.OutputWidth >= project.OutputHeight ? "1536x1024" : "1024x1536",
            ImageDeployment = string.IsNullOrWhiteSpace(project.ImageModel)
                ? imageGenerator.Deployment
                : project.ImageModel,
            CurrentAsset = null,
            CurrentAssetContent = null,
            EnforceProjectVideoCanvas = true,
            ForcedVideoWidth = canvas.Width,
            ForcedVideoHeight = canvas.Height,
            EventWriter = static (_, _) => ValueTask.CompletedTask
        };
        var tools = toolRegistry.CreateTools(
            context,
            new HashSet<string>([
                "list_project_resources", "query_storyboard", "read_project_resource_contents",
                "inspect_remote_comfyui", "manage_remote_comfyui", "generate_comfyui_videos_batch"
            ], StringComparer.OrdinalIgnoreCase)).ToList();
        var skillNames = new[] { "minimax-h3-video", "minimax-h3-video-prompt" };
        var skillPaths = skillNames.Select(name =>
            Path.GetDirectoryName(skillCatalog.Get(name)?.FilePath
                ?? throw new InvalidOperationException($"生产阶段缺少技能：{name}。"))!)
            .ToArray();
        var targets = string.Join(
            Environment.NewLine,
            items.OrderBy(item => item.ShotName).Select(item =>
                $"- shotAssetId={item.ShotAssetId}; shotResourceId={item.ShotResourceId}; name={item.ShotName}"));
        var instruction = $"""
            为下面全部 {items.Count} 个待处理 shot 生成视频。必须先逐个读取完整 shot 正文和当前首尾帧，
            为全部 shot 完成 minimax-h3-video-prompt 交接检查；只有全部提示词准备完成后，才调用一次
            generate_comfyui_videos_batch，把所有任务组成同一个 videoJobsJson 数组。禁止调用 generate_comfyui_video，
            禁止拆批或逐镜提交。共享参数使用同一个可用 MiniMax H3 API workflow、frameFitMode=cover、fps=24。
            批量工具会强制使用项目统一画布 {canvas.Width}x{canvas.Height}，不得传入或建议其他分辨率。

            目标 shot：
            {targets}
            """;

        await foreach (var _ in directorAgent.StreamReplyWithToolsAsync(
            [],
            instruction,
            $"当前项目 ID：{run.ProjectId}；项目快速拉片规格：{project.PreviewResolution}；统一 H3 画布：{canvas.Width}x{canvas.Height}。",
            project.LanguageModel,
            tools,
            skillPaths,
            cancellationToken))
        {
        }

        if (!context.BatchVideoGenerationInvoked)
            throw new InvalidOperationException("Agent 未调用批量视频生成工具。");

        var generatedIds = context.RevisedAssets.Select(asset => asset.Id).ToHashSet();
        var outputs = new Dictionary<Guid, Guid>(items.Count);
        foreach (var item in items)
        {
            var output = await FindBoundAssetAsync(item, Stages["videos"], generatedIds, cancellationToken)
                ?? throw new InvalidOperationException($"批量工具未为 {item.ShotName} 持久化有效 video 绑定。");
            outputs[item.Id] = output.Id;
        }
        return outputs;
    }

    public async Task<Guid> AssembleAsync(ProductionRun run, CancellationToken cancellationToken)
    {
                using var spec = JsonDocument.Parse(run.SpecJson);
                var shotNameContains = spec.RootElement.TryGetProperty("shotNameContains", out var filter)
                    && filter.ValueKind == JsonValueKind.String
                        ? filter.GetString() ?? string.Empty
                        : string.Empty;
        var project = await dbContext.Projects.AsNoTracking().SingleAsync(
            project => project.Id == run.ProjectId,
            cancellationToken);
        var canvas = GetRunCanvas(run, project.PreviewResolution);
        using var context = new DirectorToolContext
        {
            ProjectId = run.ProjectId,
            Content = "组装当前项目完整成片，要求每镜都有旁白。",
            RequestedModel = project.LanguageModel,
            ImageSize = "1536x1024",
            ImageDeployment = imageGenerator.Deployment,
            CurrentAsset = null,
            CurrentAssetContent = null,
            EnforceProjectVideoCanvas = true,
            ForcedVideoWidth = canvas.Width,
            ForcedVideoHeight = canvas.Height,
            EventWriter = static (_, _) => ValueTask.CompletedTask
        };
        var tools = toolRegistry.CreateTools(
            context,
            new HashSet<string>(["list_project_resources", "read_project_resources",
                "read_project_resource_contents", "generate_speech", "bind_shot_asset", "assemble_project_video"],
                StringComparer.OrdinalIgnoreCase)).ToList();
        var skill = skillCatalog.Get("final-video-assembly")
            ?? throw new InvalidOperationException("缺少 final-video-assembly 技能。");
        await foreach (var _ in directorAgent.StreamReplyWithToolsAsync(
            [],
            $"加载 final-video-assembly，使用 {canvas.Width}x{canvas.Height}、24 FPS、requireNarration=true 一次组装最终 MP4；shotNameContains 传‘{shotNameContains}’。",
            $"当前项目 ID：{run.ProjectId}。所有输入必须从持久化 shot 绑定读取。",
            project.LanguageModel,
            tools,
            [Path.GetDirectoryName(skill.FilePath)!],
            cancellationToken))
        {
        }
        var output = context.RevisedAssets
            .LastOrDefault(asset => asset.ContentType == "video/mp4")
            ?? throw new InvalidOperationException("Agent 未持久化最终 MP4。");
        return output.Id;
    }

    private async Task<Asset?> FindBoundAssetAsync(
        ProductionRunItem item,
        StageDefinition stage,
        HashSet<Guid>? allowedAssetIds,
        CancellationToken cancellationToken)
    {
        var bindings = await (from link in dbContext.ShotAssetLinks.AsNoTracking()
                              join asset in dbContext.Assets.AsNoTracking() on link.AssetId equals asset.Id
                              where link.ProjectId == item.ProjectId
                                  && link.ShotResourceId == item.ShotResourceId
                                  && link.Role == stage.Role
                                  && asset.ProjectId == item.ProjectId
                                  && asset.Type == "media"
                                  && asset.ContentType.StartsWith(stage.ContentTypePrefix)
                              select new { Asset = asset, link.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var assets = bindings
            .OrderByDescending(binding => binding.CreatedAtUtc)
            .Select(binding => binding.Asset);
        return allowedAssetIds is null
            ? assets.FirstOrDefault()
            : assets.FirstOrDefault(asset => allowedAssetIds.Contains(asset.Id));
    }

    private static bool HasVideoCanvas(Asset asset, ProjectVideoCanvas canvas)
    {
        if (string.IsNullOrWhiteSpace(asset.GenerationMetadataJson))
            return false;
        try
        {
            using var metadata = JsonDocument.Parse(asset.GenerationMetadataJson);
            var parameters = metadata.RootElement.GetProperty("parameters");
            return parameters.GetProperty("width").GetInt32() == canvas.Width
                && parameters.GetProperty("height").GetInt32() == canvas.Height;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private static ProjectVideoCanvas GetRunCanvas(ProductionRun run, string previewResolution)
    {
        try
        {
            using var spec = JsonDocument.Parse(run.SpecJson);
            var canvas = spec.RootElement.GetProperty("videoCanvas");
            return new ProjectVideoCanvas(
                canvas.GetProperty("width").GetInt32(),
                canvas.GetProperty("height").GetInt32());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ProjectVideoCanvas.FromPreviewResolution(previewResolution);
        }
    }

    private sealed record StageDefinition(
        IReadOnlyList<string> SkillNames,
        IReadOnlyList<string> ToolNames,
        string Role,
        string ContentTypePrefix,
        string Instruction);
}