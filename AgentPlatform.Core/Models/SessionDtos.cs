namespace AgentPlatform.Core.Models;

public sealed record SessionSummaryDto(
    string SessionId,
    string AgentId,
    string AgentName,
    string Title,
    string Preview,
    string? LastMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string? ParentSessionId = null,
    string? ForkedFromMessageId = null,
    int? ForkedFromSequence = null,
    string BranchKind = "root");

public sealed record SessionDetailsDto(
    string SessionId,
    string AgentId,
    string AgentName,
    string Title,
    string Preview,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> MiddlewareIds,
    IReadOnlyList<string> SkillIds,
    ContextPolicyDto ContextPolicy,
    string ContextMode,
    string ContextProfile,
    ContextCompactionStatsDto? LastCompactionStats,
    ThinkingPolicyDto ThinkingPolicy,
    string ThinkingMode,
    string ThinkingCapture,
    int ReasoningTraceCount,
    int LastReasoningTokenEstimate,
    ModelDefinitionDto Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageDto> Messages,
    string? ParentSessionId = null,
    string? ForkedFromMessageId = null,
    int? ForkedFromSequence = null,
    string BranchKind = "root");

public sealed record ChatMessageDto(
    string Id,
    string Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PagedMessagesDto(
    IReadOnlyList<ChatMessageDto> Items,
    int Limit,
    int Offset,
    int Total);

public sealed record ForkSessionRequest(string? Title = null);

public sealed record EditMessageRequest(string Message);
