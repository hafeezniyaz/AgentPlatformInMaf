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
    int MessageCount);

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
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageDto> Messages);

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
