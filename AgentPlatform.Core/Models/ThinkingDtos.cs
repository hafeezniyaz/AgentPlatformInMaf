using System.Text.Json;

namespace AgentPlatform.Core.Models;

public sealed record ThinkingPolicyDto
{
    public bool? Enabled { get; init; }

    public string? Mode { get; init; }

    public string? Capture { get; init; }

    public bool? ExposeToClient { get; init; }

    public int? MaxPreservedTokens { get; init; }

    public JsonElement? RawRequestOptionsJson { get; init; }
}

public sealed record ThinkingCapabilitiesDto(
    IReadOnlyList<string> SupportedModes,
    IReadOnlyList<string> SupportedCaptures,
    ThinkingPolicyDto DefaultPolicy);

public sealed record ReasoningTraceDto(
    string Id,
    string SessionId,
    string? MessageId,
    int TurnSequence,
    string Role,
    string Model,
    string ReasoningContentJson,
    int TokenEstimate,
    string CaptureMode,
    DateTimeOffset CreatedAt);

public sealed record ReasoningTraceWriteDto(
    string SessionId,
    string? MessageId,
    int TurnSequence,
    string Role,
    string Model,
    string ReasoningContentJson,
    int TokenEstimate,
    string CaptureMode);
