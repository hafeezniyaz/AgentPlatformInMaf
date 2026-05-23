namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class ChatSessionEntity
{
    public string SessionId { get; set; } = default!;

    public string AgentId { get; set; } = default!;

    public string AgentName { get; set; } = default!;

    public string Title { get; set; } = default!;

    public string Preview { get; set; } = "";

    public string ConfigHash { get; set; } = default!;

    public string ToolIdsJson { get; set; } = "[]";

    public string MiddlewareIdsJson { get; set; } = "[]";

    public string SkillIdsJson { get; set; } = "[]";

    public string? SerializedSessionState { get; set; }

    public string ContextPolicyJson { get; set; } = "{}";

    public string ContextMode { get; set; } = "inFlight";

    public string ContextProfile { get; set; } = "balanced";

    public string? CompactedPromptSnapshotJson { get; set; }

    public string? LastCompactionStatsJson { get; set; }

    public DateTimeOffset? LastCompactedAt { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
