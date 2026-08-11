using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlexDirectorConsole.Api.Services;

public sealed record ComfyUiVideoRequest(
    int LocalPort,
    string WorkflowJson,
    byte[] FirstFrame,
    byte[]? LastFrame,
    string Prompt,
    int Width,
    int Height,
    int FrameCount,
    int Fps,
    string ResourceName);

public sealed record GeneratedVideo(byte[] Bytes, string FileName, string ContentType);

public interface IComfyUiVideoGenerator
{
    Task<GeneratedVideo> GenerateAsync(ComfyUiVideoRequest request, CancellationToken cancellationToken);
}

public sealed class ComfyUiVideoGenerator(HttpClient httpClient) : IComfyUiVideoGenerator
{
    public async Task<GeneratedVideo> GenerateAsync(ComfyUiVideoRequest request, CancellationToken cancellationToken)
    {
        var baseUri = new Uri($"http://127.0.0.1:{request.LocalPort}");
        var prefix = $"alex-{Guid.NewGuid():N}";
        var firstFrameName = await UploadImageAsync(baseUri, request.FirstFrame, $"{prefix}-first.png", cancellationToken);
        var lastFrameName = request.LastFrame is null
            ? firstFrameName
            : await UploadImageAsync(baseUri, request.LastFrame, $"{prefix}-last.png", cancellationToken);
        var workflow = JsonNode.Parse(request.WorkflowJson)
            ?? throw new InvalidOperationException("Workflow JSON 为空。");
        ReplaceTokens(workflow, new Dictionary<string, JsonNode?>
        {
            ["{{FIRST_FRAME}}"] = firstFrameName,
            ["{{LAST_FRAME}}"] = lastFrameName,
            ["{{PROMPT}}"] = request.Prompt,
            ["{{WIDTH}}"] = request.Width,
            ["{{HEIGHT}}"] = request.Height,
            ["{{FRAME_COUNT}}"] = request.FrameCount,
            ["{{FPS}}"] = request.Fps,
            ["{{OUTPUT_PREFIX}}"] = prefix
        });
        var unresolved = workflow.ToJsonString().Contains("{{", StringComparison.Ordinal);
        if (unresolved) throw new InvalidOperationException("Workflow 仍含未解析的 {{...}} 占位符。");

        using var submitResponse = await httpClient.PostAsJsonAsync(new Uri(baseUri, "/prompt"), new { prompt = workflow }, cancellationToken);
        var submitJson = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!submitResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ComfyUI 拒绝 workflow：{submitJson}");
        }
        var promptId = submitJson?["prompt_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("ComfyUI 未返回 prompt_id。");

        for (var attempt = 0; attempt < 1800; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var history = await httpClient.GetFromJsonAsync<JsonObject>(new Uri(baseUri, $"/history/{promptId}"), cancellationToken);
            var record = history?[promptId] as JsonObject;
            if (record is null) continue;
            var output = FindVideoOutput(record["outputs"]);
            if (output is null)
            {
                var status = record["status"]?["status_str"]?.GetValue<string>();
                if (status == "error") throw new InvalidOperationException($"ComfyUI 视频任务失败：{record["status"]}");
                continue;
            }
            var fileName = output["filename"]?.GetValue<string>() ?? throw new InvalidOperationException("ComfyUI 输出缺少文件名。");
            var subfolder = output["subfolder"]?.GetValue<string>() ?? string.Empty;
            var type = output["type"]?.GetValue<string>() ?? "output";
            var bytes = await httpClient.GetByteArrayAsync(new Uri(baseUri, $"/view?filename={Uri.EscapeDataString(fileName)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}"), cancellationToken);
            if (bytes.Length < 1024 || bytes.Length < 12 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            {
                throw new InvalidOperationException("下载结果不是有效且大小合理的 MP4 文件。");
            }
            return new GeneratedVideo(bytes, fileName, "video/mp4");
        }
        throw new TimeoutException("等待 ComfyUI 视频生成超时。");
    }

    private async Task<string> UploadImageAsync(Uri baseUri, byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("image/png");
        form.Add(content, "image", fileName);
        form.Add(new StringContent("true"), "overwrite");
        using var response = await httpClient.PostAsync(new Uri(baseUri, "/upload/image"), form, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"上传关键帧失败：{result}");
        return result?["name"]?.GetValue<string>() ?? fileName;
    }

    private static JsonObject? FindVideoOutput(JsonNode? outputs)
    {
        if (outputs is not JsonObject nodes) return null;
        foreach (var node in nodes)
        {
            if (node.Value is not JsonObject output) continue;
            foreach (var key in new[] { "videos", "gifs", "images" })
            {
                if (output[key] is not JsonArray files) continue;
                foreach (var file in files.OfType<JsonObject>())
                {
                    if (file["filename"]?.GetValue<string>().EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) == true) return file;
                }
            }
        }
        return null;
    }

    private static void ReplaceTokens(JsonNode node, IReadOnlyDictionary<string, JsonNode?> replacements)
    {
        if (node is JsonObject valueObject)
        {
            foreach (var property in valueObject.ToArray())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && replacements.TryGetValue(text, out var replacement))
                    valueObject[property.Key] = replacement?.DeepClone();
                else if (property.Value is not null) ReplaceTokens(property.Value, replacements);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value && value.TryGetValue<string>(out var text) && replacements.TryGetValue(text, out var replacement))
                    array[index] = replacement?.DeepClone();
                else if (array[index] is not null) ReplaceTokens(array[index]!, replacements);
            }
        }
    }
}