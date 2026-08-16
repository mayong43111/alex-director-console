using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public interface IFoundryConnectionTester
{
    Task TestAsync(
        string endpoint,
        string deployment,
        string apiKey,
        CancellationToken cancellationToken);
}

public sealed class AzureFoundryConnectionTester : IFoundryConnectionTester
{
    public async Task TestAsync(
        string endpoint,
        string deployment,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        var chatClient = client.GetChatClient(deployment);
        await chatClient.CompleteChatAsync(
            [new UserChatMessage("Reply with OK.")],
            new ChatCompletionOptions { MaxOutputTokenCount = 8 },
            cancellationToken);
    }
}