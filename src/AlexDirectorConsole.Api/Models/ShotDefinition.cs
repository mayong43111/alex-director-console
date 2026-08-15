namespace AlexDirectorConsole.Api.Models;

public sealed class ShotDefinition
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid ShotResourceId { get; set; }

    public Guid ScriptResourceId { get; set; }

    public int SceneNumber { get; set; }

    public int ShotNumber { get; set; }

    public double DurationSeconds { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}