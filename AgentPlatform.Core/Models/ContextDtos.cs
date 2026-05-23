namespace AgentPlatform.Core.Models;

public sealed record ContextPolicyDto
{
    public bool? Enabled { get; init; }

    public string? Mode { get; init; }

    public string? Profile { get; init; }

    public string? SummarizerModel { get; init; }

    public int? ToolResultTokenThreshold { get; init; }

    public int? ToolResultMinimumPreservedGroups { get; init; }

    public int? SummarizationTokenThreshold { get; init; }

    public int? SummarizationMinimumPreservedGroups { get; init; }

    public int? SlidingWindowMaxTurns { get; init; }

    public int? SlidingWindowMinimumPreservedTurns { get; init; }

    public int? TruncationTokenThreshold { get; init; }

    public int? TruncationMinimumPreservedGroups { get; init; }

    public string? SummarizationPrompt { get; init; }
}

public sealed record ContextCapabilitiesDto(
    IReadOnlyList<string> SupportedModes,
    IReadOnlyList<string> SupportedProfiles,
    ContextPolicyDto DefaultPolicy);

public sealed record ContextCompactionStatsDto(
    int SourceMessageCount,
    int CompactedMessageCount,
    string Mode,
    string Profile,
    DateTimeOffset CompactedAt);
