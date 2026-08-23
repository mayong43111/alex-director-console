using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.Projects.Settings;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Assets;

public sealed record VisualReferencePromptWriterRequest(
    JsonElement ProjectContext,
    string SubjectKind,
    string TargetImageModel,
    string ModelSize,
    bool IsImageEdit,
    string? PreviousPrompt,
    string? Instruction);

public sealed record VisualReferencePromptWriterResult(
    string Prompt,
    string Model,
    string Runtime);

public interface IVisualReferencePromptWriter
{
    Task<VisualReferencePromptWriterResult> WriteAsync(
        VisualReferencePromptWriterRequest request,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafVisualReferencePromptWriter(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IVisualReferencePromptWriter
{
    private const string Instructions = """
        You are a production visual-development prompt director.
        Write one final English prompt optimized specifically for the target image model named in the request.
        Synthesize the complete project and subject context into a coherent prompt; never concatenate fields or append revision instructions verbatim.
        When a previous prompt is supplied, rewrite it according to the director request while preserving constraints that were not changed.
        Adapt syntax, density, ordering, and edit language to the target model. Follow the target-model guidance in the request.
        Character references must be reusable identity sheets. Scene references must be reusable environment sheets. Prop references must isolate one reusable object.
        Preserve all mandatory details and explicitly exclude forbidden details. Do not invent story facts that conflict with the context.
        The requested multi-view layout is intentional, but do not add titles, labels, captions, arrows, dimensions, logos, watermarks, UI, or readable text.
        Return only the final image-generation prompt, without Markdown, labels, explanations, alternatives, or JSON.
        """;

    public async Task<VisualReferencePromptWriterResult> WriteAsync(
        VisualReferencePromptWriterRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型，用于编写模型适配的设定图提示词。");
        }

        var agent = LlmChatClientFactory.Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(new HarnessAgentOptions
            {
                Name = "AlexVisualReferencePromptWriter",
                MaxContextWindowTokens = 1_050_000,
                MaxOutputTokens = 4_096,
                MaximumIterationsPerRequest = 2,
                DisableFileMemory = true,
                DisableWebSearch = true,
                DisableTodoProvider = true,
                DisableAgentModeProvider = true,
                DisableAgentSkillsProvider = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = Instructions,
                    MaxOutputTokens = 4_096
                }
            }, loggerFactory);
        var response = await agent.RunAsync(
            $$"""
            Target image model: {{request.TargetImageModel}}
            Generation mode: {{(request.IsImageEdit ? "image edit using the current approved reference" : "text to image")}}
            Target model size: {{request.ModelSize}}
            Subject type: {{request.SubjectKind}}
            Target-model guidance: {{ModelGuidance(request.TargetImageModel, request.IsImageEdit)}}

            Complete project and subject context:
            {{request.ProjectContext.GetRawText()}}

            Previous approved prompt:
            {{request.PreviousPrompt?.Trim() ?? "None. Create the first prompt from the supplied context."}}

            Director's current request:
            {{request.Instruction?.Trim() ?? "No additional request. Produce the best initial production-reference prompt."}}
            """,
            cancellationToken: cancellationToken);
        var prompt = response.Text?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
            throw new InvalidOperationException($"{request.TargetImageModel} 提示词 Agent 未返回内容。");
        return new(prompt, LlmChatClientFactory.GetModel(configuration!), "MAF HarnessAgent");
    }

    private static string ModelGuidance(string targetImageModel, bool isImageEdit)
    {
        if (string.Equals(targetImageModel, FoundryConfigurationView.ComfyUiTextToImageModel, StringComparison.OrdinalIgnoreCase))
            return "For Krea 2 Turbo, lead with the subject and sheet layout, use compact concrete visual phrases, then style, materials, lighting, and negative constraints. Avoid long prose and conflicting adjectives.";
        if (string.Equals(targetImageModel, FoundryConfigurationView.ComfyUiImageEditModel, StringComparison.OrdinalIgnoreCase))
            return "For Qwen Image Edit 2511, state exactly what must change and what must remain identical to the supplied reference. Use direct edit instructions and preserve identity, geometry, costume, materials, colors, and layout unless explicitly changed.";
        return isImageEdit
            ? "For GPT Image, write explicit natural-language edit instructions. Identify preserved reference features first, then requested changes, spatial layout, materials, lighting, and exclusions."
            : "For GPT Image, use structured natural language with explicit subject, composition, spatial relationships, materials, lighting, continuity constraints, and exclusions.";
    }
}
#pragma warning restore MAAI001