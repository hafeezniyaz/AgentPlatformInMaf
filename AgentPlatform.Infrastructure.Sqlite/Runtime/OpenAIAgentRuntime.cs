using System.ComponentModel;
using System.Data;
using System.Text;
using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Runtime;
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
    ContextCompactionProviderFactory compactionProviderFactory) : IAgentRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async IAsyncEnumerable<RuntimeStreamEvent> StreamAsync(
        AgentRunSpec run,
        string? serializedSessionState,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        var client = new OpenAIClient(apiKey);
        var chatClient = client.GetChatClient(run.Model).AsIChatClient();
        var tools = BuildTools(run.ToolIds);
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

        var startedAt = DateTimeOffset.UtcNow;
        var assistantMessage = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(run.Message, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                assistantMessage.Append(update.Text);
                yield return new RuntimeStreamEvent("text.delta", new TextDeltaPayload(update.Text));
            }
        }

        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        var completed = new RunCompletedPayload(assistantMessage.ToString());
        var elapsed = DateTimeOffset.UtcNow - startedAt;
        object payload = run.MiddlewareIds.Contains("timing", StringComparer.OrdinalIgnoreCase)
            ? new { completed.AssistantMessage, elapsedMs = elapsed.TotalMilliseconds }
            : completed;

        yield return new RuntimeStreamEvent("run.completed", payload, serialized.GetRawText());
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

    private IList<AITool> BuildTools(IReadOnlyList<string> toolIds)
    {
        var tools = new List<AITool>();

        foreach (var toolId in toolIds)
        {
            switch (toolId)
            {
                case "clock":
                    tools.Add(AIFunctionFactory.Create(GetCurrentUtcTime));
                    break;
                case "calculator":
                    tools.Add(AIFunctionFactory.Create(Calculate));
                    break;
                case "weather":
                    tools.Add(AIFunctionFactory.Create(GetWeather));
                    break;
            }
        }

        return tools;
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
            .UseFileSkill(skillsPath)
            .UseFilter(skill => selected.Contains(skill.Frontmatter.Name))
            .Build();
#pragma warning restore MAAI001

        return [provider];
    }

    [Description("Get the current UTC timestamp in ISO-8601 format.")]
    private static string GetCurrentUtcTime()
        => DateTimeOffset.UtcNow.ToString("O");

    [Description("Evaluate a simple arithmetic expression containing numbers, parentheses, and +, -, *, / operators.")]
    private static string Calculate([Description("Arithmetic expression to evaluate.")] string expression)
    {
        if (expression.Any(character => !"0123456789.+-*/() ".Contains(character)))
        {
            return "Only simple arithmetic expressions are allowed.";
        }

        try
        {
            var result = new DataTable().Compute(expression, null);
            return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
        catch (Exception ex)
        {
            return $"Could not evaluate expression: {ex.Message}";
        }
    }

    [Description("Get a demo weather report for a city. This is a local stub for development.")]
    private static string GetWeather([Description("City or location name.")] string location)
        => $"The weather in {location} is pleasant with light clouds. This is demo data.";
}
