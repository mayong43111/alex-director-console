namespace AlexDirectorConsole.Api.Models;

public sealed class Asset
{
    public Guid Id { get; set; }

    public Guid ResourceId { get; set; }

    public int Version { get; set; } = 1;

    public Guid ProjectId { get; set; }

    public required string Type { get; set; }

    public required string Name { get; set; }

    public required string BlobKey { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public string? GenerationMetadataJson { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}