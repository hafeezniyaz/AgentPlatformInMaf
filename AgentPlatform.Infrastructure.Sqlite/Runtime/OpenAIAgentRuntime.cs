using System.Text;
using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
using AgentPlatform.Core.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;

namespace AgentPlatform.Infrastructure.Sqlite.Runtime;

public sealed class OpenAIAgentRuntime(
    IConfiguration configuration,
    IOptions<AgentPlatformOptions> options,
    ILoggerFactory loggerFactory,
    IServiceProvider serviceProvider,
    SqliteChatHistoryProvider chatHistoryProvider,
    ContextCompactionProviderFactory compactionProviderFactory,
    IAgentToolRegistry toolRegistry) : IAgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        AgentRunSpec run,
        string? serializedSessionState,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) &&
            !string.Equals(run.ResolvedModel.Provider, "vllm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var chatClient = CreateChatClient(run, apiKey);
        var tools = toolRegistry.ResolveTools(run.ToolIds, serviceProvider).ToList();
        var contextProviders = BuildSkillProviders(run.SkillIds);

        var agentOptions = new ChatClientAgentOptions
        {
            Id = run.Agent.Id,
            Name = run.Agent.Name,
            Description = run.Agent.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = run.Instructions,
                Tools = tools
            },
            AIContextProviders = contextProviders,
            ChatHistoryProvider = chatHistoryProvider
        };

        AIAgent agent = CreateAgent(chatClient, agentOptions, run.ContextPolicy);
        AgentSession session = string.IsNullOrWhiteSpace(serializedSessionState)
            ? await agent.CreateSessionAsync(cancellationToken)
            : await agent.DeserializeSessionAsync(JsonDocument.Parse(serializedSessionState).RootElement, cancellationToken: cancellationToken);
        session.StateBag.SetValue(SqliteChatHistoryProvider.SessionIdStateKey, run.SessionId);
        session.StateBag.SetValue(SqliteChatHistoryProvider.ContextModeStateKey, run.ContextPolicy.Mode ?? "inFlight");
        session.StateBag.SetValue(SqliteChatHistoryProvider.ContextPolicyStateKey, JsonSerializer.Serialize(run.ContextPolicy, JsonOptions));
        session.StateBag.SetValue(SqliteChatHistoryProvider.ThinkingModeStateKey, run.ThinkingPolicy.Mode ?? "disabled");
        session.StateBag.SetValue(SqliteChatHistoryProvider.ModelCompatibilityGroupStateKey, run.ResolvedModel.CompatibilityGroup);

        var startedAt = DateTimeOffset.UtcNow;
        var assistantMessage = new StringBuilder();
        var reasoningStarted = false;

        await foreach (var update in agent.RunStreamingAsync(run.Message, session, cancellationToken: cancellationToken))
        {
            foreach (var reasoning in update.Contents.OfType<TextReasoningContent>())
            {
                if (!string.IsNullOrEmpty(reasoning.Text))
                {
                    if (!reasoningStarted)
                    {
                        reasoningStarted = true;
                        yield return new RuntimeStreamEvent(
                            "reasoning.started",
                            new { },
                            ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
                    }

                    yield return new RuntimeStreamEvent(
                        "reasoning.delta",
                        new ReasoningDeltaPayload(reasoning.Text),
                        ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
                }
            }

            if (!string.IsNullOrEmpty(update.Text))
            {
                assistantMessage.Append(update.Text);
                yield return new RuntimeStreamEvent("text.delta", new TextDeltaPayload(update.Text));
            }
        }

        if (reasoningStarted)
        {
            yield return new RuntimeStreamEvent(
                "reasoning.completed",
                new { },
                ExposeToClient: run.ThinkingPolicy.ExposeToClient ?? false);
        }

        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var completed = new RunCompletedPayload(assistantMessage.ToString());
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        object payload = run.MiddlewareIds.Contains("timing", StringComparer.OrdinalIgnoreCase)
            ? new { completed.AssistantMessage, elapsedMs = elapsed.TotalMilliseconds }
            : completed;

        yield return new RuntimeStreamEvent("run.completed", payload, serialized.GetRawText());
    }

    private IChatClient CreateChatClient(AgentRunSpec run, string? apiKey)
    {
        if (string.Equals(run.ResolvedModel.Provider, "vllm", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(run.ResolvedModel.BaseUrl))
            {
                throw new InvalidOperationException($"Model '{run.ResolvedModel.Id}' is configured for vLLM but has no BaseUrl.");
            }

            var endpoint = new Uri(run.ResolvedModel.BaseUrl.TrimEnd('/') + "/");
            var httpClient = new HttpClient { BaseAddress = endpoint };
            var vllmApiKey = configuration["VLLM_API_KEY"] ?? Environment.GetEnvironmentVariable("VLLM_API_KEY");
            if (!string.IsNullOrWhiteSpace(vllmApiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", vllmApiKey);
            }

            return new VllmOpenAICompatibleChatClient(httpClient, endpoint, run.ResolvedModel.Id, run.ThinkingPolicy);
        }

        var client = new OpenAIClient(apiKey);
        return client.GetChatClient(run.Model).AsIChatClient();
    }

    private AIAgent CreateAgent(IChatClient chatClient, ChatClientAgentOptions agentOptions, ContextPolicyDto contextPolicy)
    {
        var compactionProvider = compactionProviderFactory.CreateInFlightProvider(contextPolicy);
        if (compactionProvider is null)
        {
            return chatClient.AsAIAgent(agentOptions, loggerFactory: loggerFactory, services: serviceProvider);
        }

        return chatClient
            .AsBuilder()
            .UseAIContextProviders(compactionProvider)
            .BuildAIAgent(agentOptions, loggerFactory, serviceProvider);
    }

    private IReadOnlyList<AIContextProvider> BuildSkillProviders(IReadOnlyList<string> skillIds)
    {
        if (skillIds.Count == 0)
        {
            return [];
        }

        var skillsPath = Path.GetFullPath(options.Value.SkillsPath);
        if (!Directory.Exists(skillsPath))
        {
            return [];
        }

        var selected = skillIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
#pragma warning disable MAAI001
        var provider = new AgentSkillsProviderBuilder()
            .UseFileSkill(skillsPath, options: CreateFileSkillOptions())
            .UseFilter(skill => selected.Contains(skill.Frontmatter.Name))
            .Build();
#pragma warning restore MAAI001

        return [provider];
    }

#pragma warning disable MAAI001
    private static AgentFileSkillsSourceOptions CreateFileSkillOptions()
        => new()
        {
            AllowedScriptExtensions = [],
            ScriptDirectories = []
        };
#pragma warning restore MAAI001
}
