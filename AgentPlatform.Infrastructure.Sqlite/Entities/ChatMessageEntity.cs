namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class ChatMessageEntity
{
    public string Id { get; set; } = default!;

    public string SessionId { get; set; } = default!;

    public string Role { get; set; } = default!;

    public string Content { get; set; } = default!;

    public int Sequence { get; set; }

    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

