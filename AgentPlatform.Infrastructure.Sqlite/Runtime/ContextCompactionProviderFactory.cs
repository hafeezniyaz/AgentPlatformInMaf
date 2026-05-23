using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

#pragma warning disable MAAI001

namespace AgentPlatform.Infrastructure.Sqlite.Runtime;

public sealed class ContextCompactionProviderFactory(
    IConfiguration configuration,
    IOptions<AgentPlatformOptions> options,
    ILoggerFactory loggerFactory)
{
    public AIContextProvider? CreateInFlightProvider(ContextPolicyDto policy)
    {
        if (policy.Enabled is false)
        {
            return null;
        }

        return new CompactionProvider(CreateStrategy(policy));
    }

    public async Task<ContextCompactionResult> CompactAsync(
        ContextPolicyDto policy,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var sourceMessages = messages.ToList();
        if (policy.Enabled is false)
        {
            return new ContextCompactionResult(sourceMessages, CreateStats(sourceMessages.Count, sourceMessages.Count, policy));
        }

        var compacted = await CompactionProvider.CompactAsync(
            CreateStrategy(policy),
            sourceMessages,
            loggerFactory.CreateLogger<ContextCompactionProviderFactory>(),
            cancellationToken);

        var compactedMessages = compacted.ToList();
        return new ContextCompactionResult(
            compactedMessages,
            CreateStats(sourceMessages.Count, compactedMessages.Count, policy));
    }

    public CompactionStrategy CreateStrategy(ContextPolicyDto policy)
    {
        var profile = policy.Profile ?? ContextPolicyResolver.ProfileBalanced;
        return profile switch
        {
            ContextPolicyResolver.ProfileCheapNoLlm => CreatePipeline(
                CreateSlidingWindowStrategy(policy),
                CreateTruncationStrategy(policy)),
            ContextPolicyResolver.ProfileSummaryFocused => CreatePipeline(
                CreateSummarizationStrategy(policy),
                CreateTruncationStrategy(policy)),
            _ => CreatePipeline(
                CreateToolResultStrategy(policy),
                CreateSummarizationStrategy(policy),
                CreateSlidingWindowStrategy(policy),
                CreateTruncationStrategy(policy))
        };
    }

    private static PipelineCompactionStrategy CreatePipeline(params CompactionStrategy[] strategies)
        => new(strategies);

    private static ToolResultCompactionStrategy CreateToolResultStrategy(ContextPolicyDto policy)
        => new(
            trigger: CompactionTriggers.TokensExceed(policy.ToolResultTokenThreshold!.Value),
            minimumPreservedGroups: policy.ToolResultMinimumPreservedGroups!.Value);

    private SummarizationCompactionStrategy CreateSummarizationStrategy(ContextPolicyDto policy)
    {
        var prompt = string.IsNullOrWhiteSpace(policy.SummarizationPrompt)
            ? null
            : policy.SummarizationPrompt;

        return prompt is null
            ? new SummarizationCompactionStrategy(
                chatClient: CreateSummarizerClient(policy),
                trigger: CompactionTriggers.TokensExceed(policy.SummarizationTokenThreshold!.Value),
                minimumPreservedGroups: policy.SummarizationMinimumPreservedGroups!.Value)
            : new SummarizationCompactionStrategy(
                chatClient: CreateSummarizerClient(policy),
                trigger: CompactionTriggers.TokensExceed(policy.SummarizationTokenThreshold!.Value),
                minimumPreservedGroups: policy.SummarizationMinimumPreservedGroups!.Value,
                summarizationPrompt: prompt);
    }

    private static SlidingWindowCompactionStrategy CreateSlidingWindowStrategy(ContextPolicyDto policy)
        => new(
            trigger: CompactionTriggers.TurnsExceed(policy.SlidingWindowMaxTurns!.Value),
            minimumPreservedTurns: policy.SlidingWindowMinimumPreservedTurns!.Value);

    private static TruncationCompactionStrategy CreateTruncationStrategy(ContextPolicyDto policy)
        => new(
            trigger: CompactionTriggers.TokensExceed(policy.TruncationTokenThreshold!.Value),
            minimumPreservedGroups: policy.TruncationMinimumPreservedGroups!.Value);

    private IChatClient CreateSummarizerClient(ContextPolicyDto policy)
    {
        var apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is required when context summarization is enabled.");
        }

        var model = string.IsNullOrWhiteSpace(policy.SummarizerModel)
            ? options.Value.DefaultModel
            : policy.SummarizerModel;

        return new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient();
    }

    private static ContextCompactionStatsDto CreateStats(int sourceCount, int compactedCount, ContextPolicyDto policy)
        => new(
            sourceCount,
            compactedCount,
            policy.Mode ?? ContextPolicyResolver.ModeInFlight,
            policy.Profile ?? ContextPolicyResolver.ProfileBalanced,
            DateTimeOffset.UtcNow);
}

public sealed record ContextCompactionResult(
    IReadOnlyList<ChatMessage> Messages,
    ContextCompactionStatsDto Stats);

#pragma warning restore MAAI001
