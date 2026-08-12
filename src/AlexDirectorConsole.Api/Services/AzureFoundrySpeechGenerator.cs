using System.Net.Http.Json;
using System.Text.Json;

namespace AlexDirectorConsole.Api.Services;

public sealed record GeneratedSpeech(
    byte[] Bytes,
    string ContentType,
    string Extension,
    string Deployment,
    string Voice,
    string ResponseFormat,
    bool InstructionsApplied);

public interface IAzureFoundrySpeechGenerator
{
    bool IsConfigured { get; }
    string ApiVersion { get; }
    string Deployment { get; }
    bool SupportsInstructions { get; }
    Task<GeneratedSpeech> GenerateAsync(
        string input,
        string voice,
        string? instructions,
        string responseFormat,
        double speed,
        CancellationToken cancellationToken = default);
}

public sealed class AzureFoundrySpeechGenerator(
    HttpClient httpClient,
    IConfiguration configuration) : IAzureFoundrySpeechGenerator
{
    private string Endpoint =>
        Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT")
        ?? configuration["AzureSpeech:Endpoint"]
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? configuration["AzureOpenAI:Endpoint"]
        ?? string.Empty;

    private string ApiKey =>
        Environment.GetEnvironmentVariable("AZURE_SPEECH_API_KEY")
        ?? configuration["AzureSpeech:ApiKey"]
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? configuration["AzureOpenAI:ApiKey"]
        ?? string.Empty;

    public string ApiVersion =>
        Environment.GetEnvironmentVariable("AZURE_SPEECH_API_VERSION")
        ?? configuration["AzureSpeech:ApiVersion"]
        ?? "2025-03-01-preview";

    public string Deployment =>
        Environment.GetEnvironmentVariable("AZURE_SPEECH_DEPLOYMENT")
        ?? configuration["AzureSpeech:Deployment"]
        ?? "tts";

    public bool SupportsInstructions =>
        !Deployment.Equals("tts", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Deployment);

    public async Task<GeneratedSpeech> GenerateAsync(
        string input,
        string voice,
        string? instructions,
        string responseFormat,
        double speed,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure Foundry speech generation is not configured.");
        }

        var endpoint = Endpoint.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(Deployment)}/audio/speech?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", ApiKey);
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = Deployment,
            ["input"] = input,
            ["voice"] = voice,
            ["response_format"] = responseFormat,
            ["speed"] = speed
        };
        if (SupportsInstructions && !string.IsNullOrWhiteSpace(instructions))
        {
            requestBody["instructions"] = instructions;
        }
        request.Content = JsonContent.Create(requestBody);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Azure speech generation failed ({(int)response.StatusCode}): {GetErrorMessage(responseBody)}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Azure speech response did not contain audio data.");
        }
        var (contentType, extension) = ResolveOutput(responseFormat);
        return new GeneratedSpeech(
            bytes,
            response.Content.Headers.ContentType?.MediaType ?? contentType,
            extension,
            Deployment,
            voice,
            responseFormat,
            SupportsInstructions && !string.IsNullOrWhiteSpace(instructions));
    }

    private static (string ContentType, string Extension) ResolveOutput(string responseFormat) =>
        responseFormat switch
        {
            "wav" => ("audio/wav", ".wav"),
            "opus" => ("audio/opus", ".opus"),
            "aac" => ("audio/aac", ".aac"),
            "flac" => ("audio/flac", ".flac"),
            "pcm" => ("audio/pcm", ".pcm"),
            _ => ("audio/mpeg", ".mp3")
        };

    private static string GetErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
        }
        return responseBody;
    }
}