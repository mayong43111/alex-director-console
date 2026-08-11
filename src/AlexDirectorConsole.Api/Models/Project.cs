namespace AlexDirectorConsole.Api.Models;

public sealed class Project
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string Description { get; set; } = string.Empty;

    public string FormatPreset { get; set; } = "16:9";

    public int OutputWidth { get; set; } = 1920;

    public int OutputHeight { get; set; } = 1080;

    public string PreviewResolution { get; set; } = "960x540";

    public string LanguageModel { get; set; } = "gpt-5.4";

    public string ImageModel { get; set; } = "gpt-image-2";

    public string VideoModel { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}