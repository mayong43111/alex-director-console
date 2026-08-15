using System.Text.Json;
using AlexDirectorConsole.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.Api.Tools;

public sealed class GenerateComfyUiVideosBatchTool(
    GenerateComfyUiVideoTool singleVideoTool,
    AppDbContext dbContext) : IDirectorTool
{
    public string Name => "generate_comfyui_videos_batch";

    public AITool Create(DirectorToolContext context) => AIFunctionFactory.Create(
        (Func<string, string, string, int, CancellationToken, Task<string>>)(async (
            videoJobsJson,
            workflowFileName,
            frameFitMode,
            fps,
            cancellationToken) =>
        {
            if (context.BatchVideoGenerationInvoked)
                throw new InvalidOperationException("同一执行会话只能调用一次批量视频生成工具。");
            context.BatchVideoGenerationInvoked = true;
            var jobs = JsonSerializer.Deserialize<VideoBatchJob[]>(videoJobsJson, context.JsonOptions)
                ?? throw new ArgumentException("videoJobsJson 必须是 JSON 数组。", nameof(videoJobsJson));
            if (jobs.Length is < 1 or > 100)
                throw new ArgumentException("批量视频任务数量必须为 1 到 100。", nameof(videoJobsJson));
            if (jobs.Any(job => string.IsNullOrWhiteSpace(job.ShotAssetId)
                || string.IsNullOrWhiteSpace(job.FirstFrameAssetId)
                || string.IsNullOrWhiteSpace(job.VideoPrompt)))
                throw new ArgumentException("每个任务必须包含 shotAssetId、firstFrameAssetId 和 videoPrompt。", nameof(videoJobsJson));
            if (jobs.Select(job => job.ShotAssetId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != jobs.Length)
                throw new ArgumentException("同一批次不能包含重复 shotAssetId。", nameof(videoJobsJson));

            var project = await dbContext.Projects
                .AsNoTracking()
                .SingleAsync(project => project.Id == context.ProjectId, cancellationToken);
            var canvas = context.ForcedVideoWidth is int forcedWidth
                && context.ForcedVideoHeight is int forcedHeight
                    ? new ProjectVideoCanvas(forcedWidth, forcedHeight)
                    : ProjectVideoCanvas.FromPreviewResolution(project.PreviewResolution);
            var function = singleVideoTool.Create(context) as AIFunction
                ?? throw new InvalidOperationException("单镜视频生成工具不可调用。");

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video.batch.started",
                message = $"开始批量生成 {jobs.Length} 个视频，统一规格 {canvas.Width}x{canvas.Height}、{fps} FPS"
            }, cancellationToken);

            var results = new List<JsonElement>(jobs.Length);
            foreach (var job in jobs)
            {
                var arguments = new AIFunctionArguments
                {
                    ["shotAssetId"] = job.ShotAssetId.Trim(),
                    ["firstFrameAssetId"] = job.FirstFrameAssetId.Trim(),
                    ["lastFrameAssetId"] = job.LastFrameAssetId?.Trim() ?? string.Empty,
                    ["frameFitMode"] = frameFitMode,
                    ["workflowFileName"] = workflowFileName,
                    ["videoPrompt"] = job.VideoPrompt.Trim(),
                    ["width"] = canvas.Width,
                    ["height"] = canvas.Height,
                    ["frameCount"] = 0,
                    ["fps"] = fps
                };
                var result = await function.InvokeAsync(arguments, cancellationToken);
                var resultJson = result?.ToString()
                    ?? throw new InvalidOperationException($"视频任务 {job.ShotAssetId} 没有返回结果。");
                results.Add(JsonSerializer.Deserialize<JsonElement>(resultJson, context.JsonOptions));
            }

            await context.WriteEventAsync(new
            {
                type = "process",
                stage = "video.batch.completed",
                message = $"批量视频生成完成：{results.Count}/{jobs.Length}，统一规格 {canvas.Width}x{canvas.Height}"
            }, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                width = canvas.Width,
                height = canvas.Height,
                fps,
                count = results.Count,
                results
            }, context.JsonOptions);
        }),
        name: Name,
        description: "一次接收完整视频提示词数组并串行生成、绑定全部 shot 视频。videoJobsJson 是 JSON 数组，每项仅含 shotAssetId、firstFrameAssetId、可选 lastFrameAssetId、videoPrompt。workflowFileName、frameFitMode、fps 为整批共享参数；宽高强制取项目 previewResolution 并对齐 H3 画布，同批任务无法使用不同分辨率。调用前必须为全部目标 shot 完成 minimax-h3-video-prompt 检查，只允许调用本工具一次，禁止再逐镜调用 generate_comfyui_video。",
        serializerOptions: context.JsonOptions);

    private sealed record VideoBatchJob(
        string ShotAssetId,
        string FirstFrameAssetId,
        string? LastFrameAssetId,
        string VideoPrompt);
}
