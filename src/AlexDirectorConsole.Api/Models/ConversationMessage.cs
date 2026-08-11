namespace AlexDirectorConsole.Api.Models;

public sealed class ConversationMessage
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public required string Role { get; set; }

    public required string Content { get; set; }

    public required string Model { get; set; }

    public string? GeneratedAssetIdsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}