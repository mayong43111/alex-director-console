using SkiaSharp;

namespace AlexDirectorConsole.V2.Api.Features.Projects.Settings;

public sealed record ProjectImageOutput(
    byte[] Bytes,
    int SourceWidth,
    int SourceHeight,
    int Width,
    int Height);

public static class ProjectImageOutputProcessor
{
    public static ProjectImageOutput FitToProjectWhenNeeded(
        byte[] bytes,
        int outputWidth,
        int outputHeight)
    {
        using var source = SKBitmap.Decode(bytes)
            ?? throw new InvalidOperationException("图片模型返回了无法读取的图片文件。");
        if (source.Width == outputWidth && source.Height == outputHeight)
        {
            return new(bytes, source.Width, source.Height, outputWidth, outputHeight);
        }
        var sourceRatio = (double)source.Width / source.Height;
        var targetRatio = (double)outputWidth / outputHeight;
        SKRect sourceRect;
        if (sourceRatio > targetRatio)
        {
            var cropWidth = (float)(source.Height * targetRatio);
            var left = (source.Width - cropWidth) / 2;
            sourceRect = new SKRect(left, 0, left + cropWidth, source.Height);
        }
        else
        {
            var cropHeight = (float)(source.Width / targetRatio);
            var top = (source.Height - cropHeight) / 2;
            sourceRect = new SKRect(0, top, source.Width, top + cropHeight);
        }

        using var target = new SKBitmap(new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(
                source,
                sourceRect,
                new SKRect(0, 0, outputWidth, outputHeight),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        using var image = SKImage.FromBitmap(target);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("项目尺寸图片编码失败。");
        return new(encoded.ToArray(), source.Width, source.Height, outputWidth, outputHeight);
    }

    public static string ModelSizeFor(
        int outputWidth,
        int outputHeight,
        string aspectRatio,
        string? imageProvider = null)
    {
        if (string.Equals(imageProvider, "comfyui", StringComparison.OrdinalIgnoreCase))
        {
            var width = RoundUpToMultiple(outputWidth, 16);
            var height = RoundUpToMultiple(outputHeight, 16);
            return $"{width}x{height}";
        }
        var requestedSize = $"{outputWidth}x{outputHeight}";
        return GptImageOptions.SupportedSizes.Contains(requestedSize)
            ? requestedSize
            : aspectRatio == "9:16" ? "1024x1536" : "1536x1024";
    }

    private static int RoundUpToMultiple(int value, int multiple) =>
        ((value + multiple - 1) / multiple) * multiple;
}

public static class GptImageOptions
{
    public const string DefaultQuality = "medium";
    public static readonly HashSet<string> SupportedQualities = ["low", "medium", "high"];
    public static readonly HashSet<string> SupportedSizes = ["1024x1024", "1536x1024", "1024x1536"];

    public static string NormalizeQuality(string? quality) =>
        quality is not null && SupportedQualities.Contains(quality)
            ? quality
            : DefaultQuality;
}