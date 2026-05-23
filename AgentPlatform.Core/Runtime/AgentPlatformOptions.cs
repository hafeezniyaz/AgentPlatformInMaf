namespace AgentPlatform.Core.Runtime;

public sealed class AgentPlatformOptions
{
    public string DefaultModel { get; set; } = "gpt-4o-mini";

    public string SkillsPath { get; set; } = "skills";

    public ContextManagementOptions Context { get; set; } = new();
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
