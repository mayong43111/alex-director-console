namespace AlexDirectorConsole.Api.Tools;

internal sealed record ProjectVideoCanvas(int Width, int Height)
{
    private const int DimensionStep = 16;

    public static ProjectVideoCanvas FromPreviewResolution(string? previewResolution)
    {
        var segments = previewResolution?
            .Split(['x', 'X', '×'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments is not { Length: 2 }
            || !int.TryParse(segments[0], out var width)
            || !int.TryParse(segments[1], out var height)
            || width is < 64 or > 4096
            || height is < 64 or > 4096)
        {
            throw new InvalidOperationException("项目快速拉片分辨率无效，必须使用宽x高格式。");
        }

        return new ProjectVideoCanvas(Align(width), Align(height));
    }

    private static int Align(int value) =>
        Math.Clamp((int)Math.Round(value / (double)DimensionStep) * DimensionStep, 64, 4096);
}
