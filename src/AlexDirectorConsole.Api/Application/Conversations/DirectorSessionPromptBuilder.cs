using System.Text;
using AlexDirectorConsole.Api.Contracts;
using AlexDirectorConsole.Api.Models;
using AlexDirectorConsole.Api.Tools;

namespace AlexDirectorConsole.Api.Application.Conversations;

internal static class DirectorSessionPromptBuilder
{
    public static string BuildAgentContext(
        SendMessageRequest request,
        DirectorToolContext toolContext,
        Asset? currentAsset,
        string? currentAssetContent,
        IReadOnlyList<Asset> recentGeneratedImages)
    {
        var selectedResourceContext = currentAsset is null
            ? "界面当前资源：未选择。可根据对话历史和最近生成图片自行确定操作对象，不要仅因界面未选择资源而要求导演重复指定。"
            : $"""
                界面当前资源（由界面选择，不需要导演重复说明）：
                - ID：{currentAsset.Id}
                - 名称：{currentAsset.Name}
                - 类型：{currentAsset.Type}
                - 文件：{currentAsset.FileName}

                当前资源完整正文：
                {currentAssetContent ?? "[非文本资源，正文不可读取]"}
                """;
        var recentImageContext = recentGeneratedImages.Count == 0
            ? "最近对话没有生成图片。"
            : $"""
                最近对话生成的图片（从新到旧，续作或修改时由 Agent 自行判断引用哪一张）：
                {string.Join(Environment.NewLine, recentGeneratedImages.Select(asset => $"- ID：{asset.Id}；名称：{asset.Name}；版本：v{asset.Version}；文件：{asset.FileName}"))}
                """;
        var projectFormatContext = $"""
            当前项目成片画面规格：
            - 项目名称：{request.ProjectName ?? "未设置"}
            - 项目描述：{request.ProjectDescription ?? "未设置"}
            - 画幅比例：{request.ProjectAspectRatio ?? "未设置"}
            - 成片分辨率：{request.ProjectResolution ?? "未设置"}
            - 快速拉片分辨率：{request.PreviewResolution ?? "未设置"}
            - Image 模型部署：{toolContext.ImageDeployment}
            - 视频模型部署：{(string.IsNullOrWhiteSpace(request.VideoModel) ? "未配置" : request.VideoModel.Trim())}
            - 成片类图片的模型原生生成尺寸：{toolContext.ImageSize}

            项目画幅只用于 shot 首帧、关键帧、分镜图和其他成片画面。人物三视图、人物设定图、场景设定图、道具设定图及其他视觉参考素材不继承项目画幅，固定使用 1:1（1024x1024）。调用图片生成或编辑工具时必须按此用途选择 imagePurpose。成片类图片的模型原生尺寸与交付分辨率不同时，按项目画幅构图，并以成片分辨率作为后期交付目标。
            """;
        return $"{projectFormatContext}\n\n{recentImageContext}\n\n{selectedResourceContext}";
    }

    public static async Task AppendPromptRecordsAsync(
        StringBuilder replyBuilder,
        DirectorToolContext toolContext,
        IDirectorSessionStream stream,
        CancellationToken cancellationToken)
    {
        if (toolContext.VideoPrompts.Count > 0)
        {
            var promptAppendix = new StringBuilder();
            foreach (var record in toolContext.VideoPrompts)
            {
                promptAppendix
                    .AppendLine()
                    .AppendLine()
                    .AppendLine($"### 生成视频完整提示词：{record.ResourceName}")
                    .AppendLine()
                    .AppendLine($"Workflow：`{record.Workflow}` · {record.Width}×{record.Height} · {record.FrameCount} 帧 · {record.Fps} FPS")
                    .AppendLine()
                    .AppendLine("```text")
                    .AppendLine(record.Prompt)
                    .Append("```");
            }
            var promptOutput = promptAppendix.ToString();
            replyBuilder.Append(promptOutput);
            await stream.WriteAsync(new { type = "assistant.delta", delta = promptOutput }, cancellationToken);
        }
    }
}