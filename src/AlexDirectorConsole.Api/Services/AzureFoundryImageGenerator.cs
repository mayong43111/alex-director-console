using System.Net.Http.Json;
using System.Text.Json;

namespace AlexDirectorConsole.Api.Services;

public sealed record GeneratedImage(
    byte[] Bytes,
    string ContentType,
    string Extension,
    string Deployment,
    string Quality,
    string? RevisedPrompt);

public sealed record ReferenceImageInput(
    byte[] Bytes,
    string ContentType,
    string FileName);

public interface IAzureFoundryImageGenerator
{
    bool IsConfigured { get; }
    string ApiVersion { get; }
    string Deployment { get; }
    string Quality { get; }
    Task<GeneratedImage> GenerateAsync(
        string prompt,
        string size,
        string deployment,
        CancellationToken cancellationToken = default);
    Task<GeneratedImage> EditAsync(
        string prompt,
        Stream sourceImage,
        string sourceContentType,
        string sourceFileName,
        string size,
        string deployment,
        CancellationToken cancellationToken = default);
    Task<GeneratedImage> GenerateFromReferencesAsync(
        string prompt,
        IReadOnlyList<ReferenceImageInput> referenceImages,
        string size,
        string deployment,
        CancellationToken cancellationToken = default);
}

public sealed class AzureFoundryImageGenerator(
    HttpClient httpClient,
    IConfiguration configuration) : IAzureFoundryImageGenerator
{
    private string Endpoint =>
        Environment.GetEnvironmentVariable("AZURE_IMAGE_ENDPOINT")
        ?? configuration["AzureImage:Endpoint"]
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
        ?? configuration["AzureOpenAI:Endpoint"]
        ?? string.Empty;

    private string ApiKey =>
        Environment.GetEnvironmentVariable("AZURE_IMAGE_API_KEY")
        ?? configuration["AzureImage:ApiKey"]
        ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
        ?? configuration["AzureOpenAI:ApiKey"]
        ?? string.Empty;

    public string ApiVersion =>
        Environment.GetEnvironmentVariable("AZURE_IMAGE_API_VERSION")
        ?? configuration["AzureImage:ApiVersion"]
        ?? "2025-04-01-preview";

    public string Deployment =>
        Environment.GetEnvironmentVariable("AZURE_IMAGE_DEPLOYMENT")
        ?? configuration["AzureImage:Deployment"]
        ?? "gpt-image-2";

    public string Quality =>
        Environment.GetEnvironmentVariable("AZURE_IMAGE_QUALITY")
        ?? configuration["AzureImage:Quality"]
        ?? "medium";

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Deployment);

    public async Task<GeneratedImage> GenerateAsync(
        string prompt,
        string size,
        string deployment,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure Foundry image generation is not configured.");
        }

        var endpoint = Endpoint.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/images/generations?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", ApiKey);
        request.Content = JsonContent.Create(new
        {
            prompt,
            n = 1,
            size,
            quality = Quality,
            output_format = "png"
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure image generation failed ({(int)response.StatusCode}): {GetErrorMessage(responseBody)}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var image = document.RootElement.GetProperty("data")[0];
        var revisedPrompt = image.TryGetProperty("revised_prompt", out var revisedPromptElement)
            ? revisedPromptElement.GetString()
            : null;
        if (image.TryGetProperty("b64_json", out var base64Element)
            && !string.IsNullOrWhiteSpace(base64Element.GetString()))
        {
            return new GeneratedImage(
                Convert.FromBase64String(base64Element.GetString()!),
                "image/png",
                ".png",
                deployment,
                Quality,
                revisedPrompt);
        }

        if (image.TryGetProperty("url", out var urlElement)
            && Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var imageUri))
        {
            using var imageResponse = await httpClient.GetAsync(imageUri, cancellationToken);
            imageResponse.EnsureSuccessStatusCode();
            var bytes = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
            var extension = contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
            return new GeneratedImage(bytes, contentType, extension, deployment, Quality, revisedPrompt);
        }

        throw new InvalidOperationException("Azure image response did not contain image data.");
    }

    public async Task<GeneratedImage> EditAsync(
        string prompt,
        Stream sourceImage,
        string sourceContentType,
        string sourceFileName,
        string size,
        string deployment,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure Foundry image generation is not configured.");
        }

        var endpoint = Endpoint.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/images/edits?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", ApiKey);
        using var form = new MultipartFormDataContent();
        using var imageContent = new StreamContent(sourceImage);
        imageContent.Headers.ContentType = new(sourceContentType);
        form.Add(imageContent, "image", sourceFileName);
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("1"), "n");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(Quality), "quality");
        form.Add(new StringContent("png"), "output_format");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure image edit failed ({(int)response.StatusCode}): {GetErrorMessage(responseBody)}");
        }

        return ParseGeneratedImage(responseBody, deployment);
    }

    public async Task<GeneratedImage> GenerateFromReferencesAsync(
        string prompt,
        IReadOnlyList<ReferenceImageInput> referenceImages,
        string size,
        string deployment,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Azure Foundry image generation is not configured.");
        }
        if (referenceImages.Count == 0)
        {
            throw new ArgumentException("At least one reference image is required.", nameof(referenceImages));
        }

        var endpoint = Endpoint.TrimEnd('/');
        var requestUri = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/images/edits?api-version={Uri.EscapeDataString(ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", ApiKey);
        using var form = new MultipartFormDataContent();
        foreach (var referenceImage in referenceImages)
        {
            var imageContent = new ByteArrayContent(referenceImage.Bytes);
            imageContent.Headers.ContentType = new(referenceImage.ContentType);
            form.Add(imageContent, "image[]", referenceImage.FileName);
        }
        form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("1"), "n");
        form.Add(new StringContent(size), "size");
        form.Add(new StringContent(Quality), "quality");
        form.Add(new StringContent("png"), "output_format");
        request.Content = form;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure reference image generation failed ({(int)response.StatusCode}): {GetErrorMessage(responseBody)}");
        }

        return ParseGeneratedImage(responseBody, deployment);
    }

    private GeneratedImage ParseGeneratedImage(string responseBody, string deployment)
    {
        using var document = JsonDocument.Parse(responseBody);
        var image = document.RootElement.GetProperty("data")[0];
        var revisedPrompt = image.TryGetProperty("revised_prompt", out var revisedPromptElement)
            ? revisedPromptElement.GetString()
            : null;
        if (image.TryGetProperty("b64_json", out var base64Element)
            && !string.IsNullOrWhiteSpace(base64Element.GetString()))
        {
            return new GeneratedImage(
                Convert.FromBase64String(base64Element.GetString()!),
                "image/png",
                ".png",
                deployment,
                Quality,
                revisedPrompt);
        }

        throw new InvalidOperationException("Azure image response did not contain embedded image data.");
    }

    private static string GetErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString() ?? "Unknown error";
        }
        catch (JsonException)
        {
            return "Unknown error";
        }
    }
}
