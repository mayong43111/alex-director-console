using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.Foundry;

public static class AzureFoundryChatClientFactory
{
    public static ChatClient Create(string endpoint, string deployment, string apiKey)
    {
        var baseEndpoint = endpoint.TrimEnd('/');
        if (!baseEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            baseEndpoint += "/openai/v1";
        }

        return new ChatClient(
            deployment,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(baseEndpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            });
    }
}