namespace AlexDirectorConsole.Api.Models;

public sealed class SkillRun
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string SkillId { get; set; } = string.Empty;
    public Guid InputAssetId { get; set; }
    public Guid? OutputAssetId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DirectorInstruction { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}