namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class PendingHumanRequestEntity
{
    public string Id { get; set; } = default!;

    public string SessionId { get; set; } = default!;

    public string? MessageId { get; set; }

    public int MessageSequence { get; set; }

    public string? RunId { get; set; }

    public string RequestType { get; set; } = default!;

    public string PayloadJson { get; set; } = "{}";

    public string Status { get; set; } = "pending";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
