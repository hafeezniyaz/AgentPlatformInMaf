namespace AgentPlatform.Core.Models;

public sealed record AgentDefinitionDto(
    string Id,
    string Name,
    string Description,
    string Source,
    string Instructions,
    string Model,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> MiddlewareIds,
    IReadOnlyList<string> SkillIds,
    IReadOnlyList<string> AllowedToolIds,
    IReadOnlyList<string> AllowedMiddlewareIds,
    IReadOnlyList<string> AllowedSkillIds,
    ContextPolicyDto? ContextPolicy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ThinkingPolicyDto? ThinkingPolicy = null);

public sealed record CreateAgentRequest(
    string Name,
    string Description,
    string Instructions,
    string? Model,
    IReadOnlyList<string>? ToolIds,
    IReadOnlyList<string>? MiddlewareIds,
    IReadOnlyList<string>? SkillIds,
    ContextPolicyDto? ContextPolicy = null,
    ThinkingPolicyDto? ThinkingPolicy = null);

public sealed record UpdateAgentRequest(
    string Name,
    string Description,
    string Instructions,
    string? Model,
    IReadOnlyList<string>? ToolIds,
    IReadOnlyList<string>? MiddlewareIds,
    IReadOnlyList<string>? SkillIds,
    ContextPolicyDto? ContextPolicy = null,
    ThinkingPolicyDto? ThinkingPolicy = null);
