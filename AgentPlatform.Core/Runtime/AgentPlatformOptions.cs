using AgentPlatform.Core.Models;

namespace AgentPlatform.Core.Runtime;

public sealed class AgentPlatformOptions
{
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    public string SkillsPath { get; set; } = "skills";

    public ContextManagementOptions Context { get; set; } = new();

    public ThinkingOptions Thinking { get; set; } = new();

    public List<ModelOptions> Models { get; set; } = [];
}

public sealed class ContextManagementOptions
{
    public bool Enabled { get; set; } = true;

    public string Mode { get; set; } = "inFlight";

    public string Profile { get; set; } = "balanced";

    public string SummarizerModel { get; set; } = "gpt-4o-mini";

    public int ToolResultTokenThreshold { get; set; } = 0x200;

    public int ToolResultMinimumPreservedGroups { get; set; } = 2;

    public int SummarizationTokenThreshold { get; set; } = 0x500;

    public int SummarizationMinimumPreservedGroups { get; set; } = 4;

    public int SlidingWindowMaxTurns { get; set; } = 20;

    public int SlidingWindowMinimumPreservedTurns { get; set; } = 1;

    public int TruncationTokenThreshold { get; set; } = 0x8000;

    public int TruncationMinimumPreservedGroups { get; set; } = 32;

    public string? SummarizationPrompt { get; set; }
}

public sealed class ThinkingOptions
{
    public bool Enabled { get; set; }

    public string Mode { get; set; } = "disabled";

    public string Capture { get; set; } = "opaque";

    public bool ExposeToClient { get; set; }

    public int MaxPreservedTokens { get; set; } = 24000;

    public string? RawRequestOptionsJson { get; set; }
}

public sealed class ModelOptions
{
    public string Id { get; set; } = "";

    public string Provider { get; set; } = "openai";

    public string? BaseUrl { get; set; }

    public string? CompatibilityGroup { get; set; }

    public int? ContextWindowTokens { get; set; }

    public ThinkingPolicyDto? ThinkingPolicy { get; set; }
}

public sealed class PrebuiltAgentCatalogOptions
{
    public List<PrebuiltAgentConfig> Agents { get; set; } = [];
}

public sealed class PrebuiltAgentConfig
{
    public string Id { get; set; } = "";

    public bool? Enabled { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Instructions { get; set; }

    public string? Model { get; set; }

    public IReadOnlyList<string>? ToolIds { get; set; }

    public IReadOnlyList<string>? MiddlewareIds { get; set; }

    public IReadOnlyList<string>? SkillIds { get; set; }

    public IReadOnlyList<string>? AllowedToolIds { get; set; }

    public IReadOnlyList<string>? AllowedMiddlewareIds { get; set; }

    public IReadOnlyList<string>? AllowedSkillIds { get; set; }

    public ContextPolicyDto? ContextPolicy { get; set; }

    public ThinkingPolicyDto? ThinkingPolicy { get; set; }
}
