namespace AlexDirectorConsole.Api.Models;

public sealed class ProductionRunItem
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ShotResourceId { get; set; }

    public Guid ShotAssetId { get; set; }

    public string ShotName { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public int Attempt { get; set; }

    public string? InputFingerprint { get; set; }

    public Guid? OutputAssetId { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}