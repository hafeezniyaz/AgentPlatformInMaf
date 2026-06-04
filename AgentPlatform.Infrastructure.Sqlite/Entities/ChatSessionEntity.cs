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

    public string AgentStateJson { get; set; } = "{}";

    public string ContextPolicyJson { get; set; } = "{}";

    public string ContextMode { get; set; } = "inFlight";

    public string ContextProfile { get; set; } = "balanced";

    public string? CompactedPromptSnapshotJson { get; set; }

    public string? LastCompactionStatsJson { get; set; }

    public DateTimeOffset? LastCompactedAt { get; set; }

    public string ThinkingPolicyJson { get; set; } = "{}";

    public string ThinkingMode { get; set; } = "disabled";

    public string ThinkingCapture { get; set; } = "opaque";

    public int ReasoningTraceCount { get; set; }

    public int LastReasoningTokenEstimate { get; set; }

    public string ModelId { get; set; } = "";

    public string ModelProvider { get; set; } = "openai";

    public string? ModelBaseUrl { get; set; }

    public string ModelCompatibilityGroup { get; set; } = "";

    public int? ModelContextWindowTokens { get; set; }

    public string? ParentSessionId { get; set; }

    public string? ForkedFromMessageId { get; set; }

    public int? ForkedFromSequence { get; set; }

    public string BranchKind { get; set; } = "root";

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
