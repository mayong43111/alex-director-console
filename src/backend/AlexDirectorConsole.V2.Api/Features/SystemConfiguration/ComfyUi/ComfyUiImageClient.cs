using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AlexDirectorConsole.V2.Api.Features.SystemConfiguration.ComfyUi;

public sealed record ComfyUiImageReference(byte[] Bytes, string ContentType);

public sealed record ComfyUiImageRequest(
    string BaseUrl,
    string WorkflowJson,
    string Prompt,
    int Width,
    int Height,
    IReadOnlyList<ComfyUiImageReference> References);

public sealed record ComfyUiGeneratedImage(byte[] Bytes, string ContentType);

public interface IComfyUiImageClient
{
    Task<ComfyUiGeneratedImage> GenerateAsync(
        ComfyUiImageRequest request,
        CancellationToken cancellationToken);
}

public interface IComfyUiImageWorkflowProvider
{
    Task<string> ReadTextToImageAsync(CancellationToken cancellationToken);
    Task<string> ReadImageEditAsync(string workflow, CancellationToken cancellationToken);
}

public sealed class PackagedComfyUiImageWorkflowProvider : IComfyUiImageWorkflowProvider
{
    public Task<string> ReadTextToImageAsync(CancellationToken cancellationToken) =>
        ReadAsync("krea-2-text-to-image-api.json", cancellationToken);

    public Task<string> ReadImageEditAsync(string workflow, CancellationToken cancellationToken) =>
        ReadAsync(ComfyUiConfigurationView.NormalizeImageEditWorkflow(workflow) switch
        {
            ComfyUiConfigurationView.Flux2DevImageEditWorkflow => "flux2-dev-image-edit-api.json",
            _ => "qwen-image-edit-2511-api.json"
        }, cancellationToken);

    private static async Task<string> ReadAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Skills",
            "image-generation",
            "workflows",
            fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("内置 ComfyUI 图片 workflow 不存在。", path);
        }
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}

public sealed class ComfyUiImageClient(IHttpClientFactory httpClientFactory) : IComfyUiImageClient
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task<ComfyUiGeneratedImage> GenerateAsync(
        ComfyUiImageRequest request,
        CancellationToken cancellationToken)
    {
        var isFlux = request.WorkflowJson.Contains("\"ReferenceLatent\"", StringComparison.Ordinal);
        var maxReferences = isFlux ? 8 : 3;
        if (request.References.Count > maxReferences)
        {
            throw new InvalidOperationException(
            $"{(isFlux ? "FLUX.2 dev" : "Qwen Image Edit 2511")} 最多支持 {maxReferences} 张参考图，当前收到 {request.References.Count} 张。");
        }
        var client = httpClientFactory.CreateClient("ComfyUiImage");
        var root = new Uri(request.BaseUrl.TrimEnd('/') + "/");
        var outputPrefix = $"alex-v2-image-{Guid.NewGuid():N}";
        var uploadedReferences = new List<string>();
        foreach (var reference in request.References)
        {
            uploadedReferences.Add(await UploadImageAsync(
                client,
                root,
                reference,
                $"{outputPrefix}-reference-{uploadedReferences.Count + 1}.png",
                cancellationToken));
        }

        var workflow = JsonNode.Parse(request.WorkflowJson)?.AsObject()
            ?? throw new InvalidOperationException("ComfyUI 图片 workflow JSON 为空。");
        ReplaceTokens(workflow, new Dictionary<string, JsonNode?>
        {
            ["{{PROMPT}}"] = request.Prompt,
            ["{{WIDTH}}"] = request.Width,
            ["{{HEIGHT}}"] = request.Height,
            ["{{SEED}}"] = Random.Shared.NextInt64(0, long.MaxValue),
            ["{{OUTPUT_PREFIX}}"] = outputPrefix,
            ["{{REFERENCE_1}}"] = uploadedReferences.ElementAtOrDefault(0),
            ["{{REFERENCE_2}}"] = uploadedReferences.ElementAtOrDefault(1),
            ["{{REFERENCE_3}}"] = uploadedReferences.ElementAtOrDefault(2),
            ["{{REFERENCE_4}}"] = uploadedReferences.ElementAtOrDefault(3),
            ["{{REFERENCE_5}}"] = uploadedReferences.ElementAtOrDefault(4),
            ["{{REFERENCE_6}}"] = uploadedReferences.ElementAtOrDefault(5),
            ["{{REFERENCE_7}}"] = uploadedReferences.ElementAtOrDefault(6),
            ["{{REFERENCE_8}}"] = uploadedReferences.ElementAtOrDefault(7)
        });
        RemoveMissingOptionalReferences(workflow, uploadedReferences.Count);
        if (workflow.ToJsonString().Contains("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ComfyUI 图片 workflow 仍含未解析占位符。");
        }

        using var submitResponse = await client.PostAsJsonAsync(
            new Uri(root, "prompt"),
            new { prompt = workflow },
            cancellationToken);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(
            cancellationToken: cancellationToken);
        if (!submitResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ComfyUI 拒绝图片 workflow：{submitBody}");
        }
        var promptId = submitBody?["prompt_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ComfyUI 未返回 prompt_id。");

        for (var attempt = 0; attempt < 900; attempt++)
        {
            var result = await GetResultAsync(client, root, promptId, cancellationToken);
            if (result.Error is not null)
            {
                throw new InvalidOperationException($"ComfyUI 图片生成失败：{result.Error}");
            }
            if (result.Output is not null)
            {
                return await DownloadAsync(client, root, result.Output, cancellationToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        throw new TimeoutException("ComfyUI 图片生成超过 30 分钟仍未完成。");
    }

    private static async Task<string> UploadImageAsync(
        HttpClient client,
        Uri root,
        ComfyUiImageReference reference,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(reference.Bytes);
        content.Headers.ContentType = new(reference.ContentType);
        form.Add(content, "image", fileName);
        form.Add(new StringContent("true"), "overwrite");
        using var response = await client.PostAsync(new Uri(root, "upload/image"), form, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"上传 ComfyUI 参考图失败：{body}");
        }
        return body?["name"]?.GetValue<string>() ?? fileName;
    }

    private static async Task<(ComfyUiImageOutput? Output, string? Error)> GetResultAsync(
        HttpClient client,
        Uri root,
        string promptId,
        CancellationToken cancellationToken)
    {
        var history = await client.GetFromJsonAsync<JsonObject>(
            new Uri(root, $"history/{Uri.EscapeDataString(promptId)}"),
            cancellationToken);
        var record = history?[promptId] as JsonObject;
        if (record is null) return (null, null);
        if (record["outputs"] is JsonObject nodes)
        {
            foreach (var node in nodes.Select(item => item.Value).OfType<JsonObject>())
            {
                if (node["images"] is not JsonArray images) continue;
                var image = images.OfType<JsonObject>().FirstOrDefault();
                if (image is null) continue;
                return (new(
                    image["filename"]?.GetValue<string>() ?? string.Empty,
                    image["subfolder"]?.GetValue<string>() ?? string.Empty,
                    image["type"]?.GetValue<string>() ?? "output"), null);
            }
        }
        var status = record["status"]?["status_str"]?.GetValue<string>();
        return string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            ? (null, record["status"]?.ToJsonString())
            : (null, null);
    }

    private static async Task<ComfyUiGeneratedImage> DownloadAsync(
        HttpClient client,
        Uri root,
        ComfyUiImageOutput output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(output.FileName))
        {
            throw new InvalidOperationException("ComfyUI 图片输出缺少文件名。");
        }
        var path = $"view?filename={Uri.EscapeDataString(output.FileName)}"
            + $"&subfolder={Uri.EscapeDataString(output.Subfolder)}"
            + $"&type={Uri.EscapeDataString(output.Type)}";
        var bytes = await client.GetByteArrayAsync(new Uri(root, path), cancellationToken);
        if (bytes.Length < PngSignature.Length
            || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidOperationException("ComfyUI 下载结果不是有效的 PNG 图片。");
        }
        return new(bytes, "image/png");
    }

    private static void RemoveMissingOptionalReferences(JsonObject workflow, int referenceCount)
    {
        if (workflow.Select(item => item.Value).OfType<JsonObject>().Any(node => string.Equals(
                node["class_type"]?.GetValue<string>(),
                "ReferenceLatent",
                StringComparison.Ordinal)))
        {
            RemoveMissingFluxReferences(workflow);
            return;
        }
        if (!string.Equals(
                workflow["8"]?["class_type"]?.GetValue<string>(),
                "TextEncodeQwenImageEditPlus",
                StringComparison.Ordinal)
            || workflow["8"]?["inputs"] is not JsonObject positive
            || workflow["9"]?["inputs"] is not JsonObject negative)
        {
            return;
        }
        if (referenceCount < 3)
        {
            workflow.Remove("3");
            positive.Remove("image3");
            negative.Remove("image3");
        }
        if (referenceCount < 2)
        {
            workflow.Remove("2");
            positive.Remove("image2");
            negative.Remove("image2");
        }
        if (referenceCount == 0)
        {
            throw new InvalidOperationException("Qwen Image Edit 2511 至少需要一张参考图。");
        }
    }

    private static void RemoveMissingFluxReferences(JsonObject workflow)
    {
        JsonArray conditioning = ["4", 0];
        var referenceNodes = workflow
            .Where(item => item.Value?["class_type"]?.GetValue<string>() == "ReferenceLatent")
            .OrderBy(item => int.Parse(item.Key, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        foreach (var referenceNode in referenceNodes)
        {
            var referenceInputs = referenceNode.Value!["inputs"]!.AsObject();
            var encodeId = referenceInputs["latent"]![0]!.GetValue<string>();
            var encodeInputs = workflow[encodeId]!["inputs"]!.AsObject();
            var scaleId = encodeInputs["pixels"]![0]!.GetValue<string>();
            var scaleInputs = workflow[scaleId]!["inputs"]!.AsObject();
            var loadId = scaleInputs["image"]![0]!.GetValue<string>();
            var image = workflow[loadId]?["inputs"]?["image"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(image))
            {
                workflow.Remove(loadId);
                workflow.Remove(scaleId);
                workflow.Remove(encodeId);
                workflow.Remove(referenceNode.Key);
                continue;
            }
            referenceInputs["conditioning"] = conditioning.DeepClone();
            conditioning = [referenceNode.Key, 0];
        }
        var guidance = workflow.Select(item => item.Value)
            .OfType<JsonObject>()
            .Single(node => node["class_type"]?.GetValue<string>() == "FluxGuidance");
        guidance["inputs"]!["conditioning"] = conditioning.DeepClone();
    }

    private static void ReplaceTokens(JsonNode node, IReadOnlyDictionary<string, JsonNode?> replacements)
    {
        if (node is JsonObject valueObject)
        {
            foreach (var property in valueObject.ToArray())
            {
                if (property.Value is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && replacements.TryGetValue(text, out var replacement))
                {
                    valueObject[property.Key] = replacement?.DeepClone();
                }
                else if (property.Value is not null)
                {
                    ReplaceTokens(property.Value, replacements);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && replacements.TryGetValue(text, out var replacement))
                {
                    array[index] = replacement?.DeepClone();
                }
                else if (array[index] is not null)
                {
                    ReplaceTokens(array[index]!, replacements);
                }
            }
        }
    }

    private sealed record ComfyUiImageOutput(string FileName, string Subfolder, string Type);
}