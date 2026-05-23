using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Services;

public sealed class ContextPolicyResolver(IOptions<AgentPlatformOptions> options) : IContextPolicyResolver
{
    public const string ModeInFlight = "inFlight";
    public const string ModePersistedPrompt = "persistedPrompt";
    public const string ProfileBalanced = "balanced";
    public const string ProfileCheapNoLlm = "cheapNoLlm";
    public const string ProfileSummaryFocused = "summaryFocused";

    public ContextCapabilitiesDto GetCapabilities()
        => new(
            SupportedModes: [ModeInFlight, ModePersistedPrompt],
            SupportedProfiles: [ProfileBalanced, ProfileCheapNoLlm, ProfileSummaryFocused],
            DefaultPolicy: GetDefaultPolicy());

    public ContextPolicyDto GetDefaultPolicy()
    {
        var context = options.Value.Context;
        return Normalize(new ContextPolicyDto
        {
            Enabled = context.Enabled,
            Mode = context.Mode,
            Profile = context.Profile,
            SummarizerModel = string.IsNullOrWhiteSpace(context.SummarizerModel)
                ? options.Value.DefaultModel
                : context.SummarizerModel,
            ToolResultTokenThreshold = context.ToolResultTokenThreshold,
            ToolResultMinimumPreservedGroups = context.ToolResultMinimumPreservedGroups,
            SummarizationTokenThreshold = context.SummarizationTokenThreshold,
            SummarizationMinimumPreservedGroups = context.SummarizationMinimumPreservedGroups,
            SlidingWindowMaxTurns = context.SlidingWindowMaxTurns,
            SlidingWindowMinimumPreservedTurns = context.SlidingWindowMinimumPreservedTurns,
            TruncationTokenThreshold = context.TruncationTokenThreshold,
            TruncationMinimumPreservedGroups = context.TruncationMinimumPreservedGroups,
            SummarizationPrompt = context.SummarizationPrompt
        });
    }

    public ContextPolicyDto Resolve(ContextPolicyDto? overridePolicy)
    {
        var defaults = GetDefaultPolicy();
        if (overridePolicy is null)
        {
            return defaults;
        }

        return Normalize(new ContextPolicyDto
        {
            Enabled = overridePolicy.Enabled ?? defaults.Enabled,
            Mode = overridePolicy.Mode ?? defaults.Mode,
            Profile = overridePolicy.Profile ?? defaults.Profile,
            SummarizerModel = overridePolicy.SummarizerModel ?? defaults.SummarizerModel,
            ToolResultTokenThreshold = overridePolicy.ToolResultTokenThreshold ?? defaults.ToolResultTokenThreshold,
            ToolResultMinimumPreservedGroups = overridePolicy.ToolResultMinimumPreservedGroups ?? defaults.ToolResultMinimumPreservedGroups,
            SummarizationTokenThreshold = overridePolicy.SummarizationTokenThreshold ?? defaults.SummarizationTokenThreshold,
            SummarizationMinimumPreservedGroups = overridePolicy.SummarizationMinimumPreservedGroups ?? defaults.SummarizationMinimumPreservedGroups,
            SlidingWindowMaxTurns = overridePolicy.SlidingWindowMaxTurns ?? defaults.SlidingWindowMaxTurns,
            SlidingWindowMinimumPreservedTurns = overridePolicy.SlidingWindowMinimumPreservedTurns ?? defaults.SlidingWindowMinimumPreservedTurns,
            TruncationTokenThreshold = overridePolicy.TruncationTokenThreshold ?? defaults.TruncationTokenThreshold,
            TruncationMinimumPreservedGroups = overridePolicy.TruncationMinimumPreservedGroups ?? defaults.TruncationMinimumPreservedGroups,
            SummarizationPrompt = overridePolicy.SummarizationPrompt ?? defaults.SummarizationPrompt
        });
    }

    private static ContextPolicyDto Normalize(ContextPolicyDto policy)
    {
        var mode = NormalizeMode(policy.Mode);
        var profile = NormalizeProfile(policy.Profile);
        var summarizerModel = string.IsNullOrWhiteSpace(policy.SummarizerModel)
            ? "gpt-4o-mini"
            : policy.SummarizerModel.Trim();

        var normalized = policy with
        {
            Enabled = policy.Enabled ?? true,
            Mode = mode,
            Profile = profile,
            SummarizerModel = summarizerModel,
            ToolResultTokenThreshold = NormalizePositive(policy.ToolResultTokenThreshold, nameof(policy.ToolResultTokenThreshold)),
            ToolResultMinimumPreservedGroups = NormalizeNonNegative(policy.ToolResultMinimumPreservedGroups, nameof(policy.ToolResultMinimumPreservedGroups)),
            SummarizationTokenThreshold = NormalizePositive(policy.SummarizationTokenThreshold, nameof(policy.SummarizationTokenThreshold)),
            SummarizationMinimumPreservedGroups = NormalizeNonNegative(policy.SummarizationMinimumPreservedGroups, nameof(policy.SummarizationMinimumPreservedGroups)),
            SlidingWindowMaxTurns = NormalizePositive(policy.SlidingWindowMaxTurns, nameof(policy.SlidingWindowMaxTurns)),
            SlidingWindowMinimumPreservedTurns = NormalizeNonNegative(policy.SlidingWindowMinimumPreservedTurns, nameof(policy.SlidingWindowMinimumPreservedTurns)),
            TruncationTokenThreshold = NormalizePositive(policy.TruncationTokenThreshold, nameof(policy.TruncationTokenThreshold)),
            TruncationMinimumPreservedGroups = NormalizeNonNegative(policy.TruncationMinimumPreservedGroups, nameof(policy.TruncationMinimumPreservedGroups)),
            SummarizationPrompt = string.IsNullOrWhiteSpace(policy.SummarizationPrompt)
                ? null
                : policy.SummarizationPrompt.Trim()
        };

        return normalized;
    }

    private static string NormalizeMode(string? mode)
        => mode?.Trim() switch
        {
            null or "" => ModeInFlight,
            var value when value.Equals(ModeInFlight, StringComparison.OrdinalIgnoreCase) => ModeInFlight,
            var value when value.Equals(ModePersistedPrompt, StringComparison.OrdinalIgnoreCase) => ModePersistedPrompt,
            _ => throw new AgentPlatformValidationException($"Unsupported context mode '{mode}'.")
        };

    private static string NormalizeProfile(string? profile)
        => profile?.Trim() switch
        {
            null or "" => ProfileBalanced,
            var value when value.Equals(ProfileBalanced, StringComparison.OrdinalIgnoreCase) => ProfileBalanced,
            var value when value.Equals(ProfileCheapNoLlm, StringComparison.OrdinalIgnoreCase) => ProfileCheapNoLlm,
            var value when value.Equals(ProfileSummaryFocused, StringComparison.OrdinalIgnoreCase) => ProfileSummaryFocused,
            _ => throw new AgentPlatformValidationException($"Unsupported context profile '{profile}'.")
        };

    private static int NormalizePositive(int? value, string propertyName)
    {
        if (value is null or <= 0)
        {
            throw new AgentPlatformValidationException($"Context policy value '{propertyName}' must be greater than zero.");
        }

        return value.Value;
    }

    private static int NormalizeNonNegative(int? value, string propertyName)
    {
        if (value is null or < 0)
        {
            throw new AgentPlatformValidationException($"Context policy value '{propertyName}' must be zero or greater.");
        }

        return value.Value;
    }
}
