namespace AgentPlatform.Core.Models;

public sealed record ModelDefinitionDto(
    string Id,
    string Provider,
    string? BaseUrl,
    string? CompatibilityGroup,
    int? ContextWindowTokens,
    ThinkingPolicyDto? ThinkingPolicy);

public sealed record ResolvedModel(
    string Id,
    string Provider,
    string? BaseUrl,
    string CompatibilityGroup,
    int? ContextWindowTokens,
    ThinkingPolicyDto? ThinkingPolicy);
