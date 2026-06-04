namespace AgentPlatform.Infrastructure.Sqlite.Entities;

public sealed class MessageCheckpointEntity
{
    public string Id { get; set; } = default!;

    public string SessionId { get; set; } = default!;

    public string MessageId { get; set; } = default!;

    public int Sequence { get; set; }

    public string? SerializedSessionState { get; set; }

    public string AgentStateJson { get; set; } = "{}";

    public string? CompactedPromptSnapshotJson { get; set; }

    public string? LastCompactionStatsJson { get; set; }

    public int ReasoningTraceCount { get; set; }

    public int LastReasoningTokenEstimate { get; set; }

    public string PendingHumanRequestsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
}
