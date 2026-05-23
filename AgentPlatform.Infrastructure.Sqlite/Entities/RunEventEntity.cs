namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class RunEventEntity
{
    public string Id { get; set; } = default!;

    public string SessionId { get; set; } = default!;

    public string EventName { get; set; } = default!;

    public string PayloadJson { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }
}

