namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class ReasoningTraceEntity
{
    public string Id { get; set; } = default!;

    public string SessionId { get; set; } = default!;

    public string? MessageId { get; set; }

    public int TurnSequence { get; set; }

    public string Role { get; set; } = "assistant";

    public string Model { get; set; } = default!;

    public string ReasoningContentJson { get; set; } = "{}";

    public int TokenEstimate { get; set; }

    public string CaptureMode { get; set; } = "opaque";

    public DateTimeOffset CreatedAt { get; set; }
}
