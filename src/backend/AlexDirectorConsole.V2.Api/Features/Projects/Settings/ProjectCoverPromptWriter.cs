using System.Text.Json;
using AlexDirectorConsole.V2.Api.Features.Projects.Generation;
using AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;
using AlexDirectorConsole.V2.Database.Data;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Settings;

public sealed record ProjectCoverPromptWriterRequest(
    JsonElement ProjectContext,
    string TargetImageModel,
    string ModelSize,
    string? PreviousPrompt,
    string? Instruction);

public sealed record ProjectCoverPromptWriterResult(
    string Prompt,
    string Model,
    string Runtime);

public interface IProjectCoverPromptWriter
{
    Task<ProjectCoverPromptWriterResult> WriteAsync(
        ProjectCoverPromptWriterRequest request,
        CancellationToken cancellationToken);
}

#pragma warning disable MAAI001
public sealed class MafProjectCoverPromptWriter(
    V2DbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    ILoggerFactory loggerFactory) : IProjectCoverPromptWriter
{
    private const string Instructions = """
        You are the Image 2 prompt director for cinematic project covers.
        Write one final English image-generation prompt optimized for the target image model in the request.
        Use the complete project context as factual and visual constraints.
        When a previous prompt is present, revise it according to the director's new request while preserving useful details that were not asked to change.
        Do not merely append the director request or concatenate project fields. Resolve conflicts and rewrite a coherent production prompt.
        The result must describe one polished, full-bleed, continuous cinematic scene with one viewpoint and clear focal hierarchy.
        Never request collages, split panels, storyboards, contact sheets, character sheets, inset images, borders, readable text, logos, watermarks, or UI.
        Return only the final prompt. Do not include Markdown, labels, explanations, alternatives, or JSON.
        """;

    public async Task<ProjectCoverPromptWriterResult> WriteAsync(
        ProjectCoverPromptWriterRequest request,
        CancellationToken cancellationToken)
    {
        var configuration = await dbContext.FoundryConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == 1, cancellationToken);
        if (!LlmChatClientFactory.IsConfigured(configuration))
        {
            throw new ProjectGenerationConfigurationException("请先在系统设置中配置语言模型，用于编写 Image 2 提示词。");
        }

        var agent = LlmChatClientFactory.Create(configuration!, dataProtectionProvider)
            .AsIChatClient()
            .AsHarnessAgent(new HarnessAgentOptions
            {
                Name = "AlexImage2PromptWriter",
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
            Target model size: {{request.ModelSize}}
            Complete project context:
            {{request.ProjectContext.GetRawText()}}

            Previous approved prompt:
            {{request.PreviousPrompt?.Trim() ?? "None. Create the first prompt from the project context."}}

            Director's current request:
            {{request.Instruction?.Trim() ?? "No additional request. Produce the best initial cover prompt."}}
            """,
            cancellationToken: cancellationToken);
        var prompt = response.Text?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            throw new InvalidOperationException("Image 2 提示词 Agent 未返回内容。");
        }
        return new(prompt, LlmChatClientFactory.GetModel(configuration!), "MAF HarnessAgent");
    }
}
#pragma warning restore MAAI001