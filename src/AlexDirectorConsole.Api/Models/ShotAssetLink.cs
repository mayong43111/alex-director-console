namespace AlexDirectorConsole.Api.Models;

public sealed class ShotAssetLink
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ShotResourceId { get; set; }

    public Guid AssetId { get; set; }

    public required string Role { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}