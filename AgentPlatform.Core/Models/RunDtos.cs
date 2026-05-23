namespace AgentPlatform.Core.Models;

public sealed record StreamRunRequest(
    string? SessionId,
    string AgentId,
    IReadOnlyList<string>? ToolIds,
    IReadOnlyList<string>? MiddlewareIds,
    IReadOnlyList<string>? SkillIds,
    string Message,
    string? Model);

public sealed record RunStreamEvent(
    string Event,
    string SessionId,
    object Data,
    DateTimeOffset Timestamp);

public sealed record TextDeltaPayload(string Text);

public sealed record RunStartedPayload(
    string AgentId,
    string Model,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> MiddlewareIds,
    IReadOnlyList<string> SkillIds,
    ContextPolicyDto ContextPolicy);

public sealed record RunCompletedPayload(string AssistantMessage);

public sealed record RunErrorPayload(string Message);
