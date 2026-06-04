using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AgentPlatform.Infrastructure.Sqlite.Runtime;

public sealed class SqliteChatHistoryProvider(
    IAgentPlatformDbContext dbContext,
    ContextCompactionProviderFactory compactionProviderFactory) : ChatHistoryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string SessionIdStateKey = "agentPlatform.sessionId";
    public const string ContextModeStateKey = "agentPlatform.context.mode";
    public const string ContextPolicyStateKey = "agentPlatform.context.policy";
    public const string ThinkingModeStateKey = "agentPlatform.thinking.mode";
    public const string ModelCompatibilityGroupStateKey = "agentPlatform.model.compatibilityGroup";

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken)
    {
        if (context.Session is null ||
            !context.Session.StateBag.TryGetValue<string>(SessionIdStateKey, out var sessionId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return [];
        }

        var mode = TryGetSessionString(context.Session, ContextModeStateKey);
        var preferCompactedSnapshot = string.Equals(
            mode,
            ContextPolicyResolver.ModePersistedPrompt,
            StringComparison.OrdinalIgnoreCase);

        var thinkingMode = TryGetSessionString(context.Session, ThinkingModeStateKey);
        var compatibilityGroup = TryGetSessionString(context.Session, ModelCompatibilityGroupStateKey);
        var includeReasoning = string.Equals(thinkingMode, ThinkingPolicyResolver.ModePreserved, StringComparison.OrdinalIgnoreCase);

        return await LoadMessagesAsync(sessionId, preferCompactedSnapshot, includeReasoning, compatibilityGroup, cancellationToken);
    }

    protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken)
    {
        if (context.InvokeException is not null ||
            context.Session is null ||
            !context.Session.StateBag.TryGetValue<string>(SessionIdStateKey, out var sessionId) ||
            string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var mode = TryGetSessionString(context.Session, ContextModeStateKey);
        if (!string.Equals(mode, ContextPolicyResolver.ModePersistedPrompt, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var policy = TryGetPolicy(context.Session) ?? await LoadStoredContextPolicyAsync(sessionId, cancellationToken);
        var previousMessages = await LoadMessagesAsync(
            sessionId,
            preferCompactedSnapshot: true,
            includeReasoning: false,
            compatibilityGroup: null,
            cancellationToken);
        var sourceMessages = previousMessages
            .Concat(context.RequestMessages)
            .Concat(context.ResponseMessages ?? [])
            .ToList();

        var compacted = await compactionProviderFactory.CompactAsync(policy, sourceMessages, cancellationToken);
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.CompactedPromptSnapshotJson = SerializeMessages(compacted.Messages);
        session.LastCompactionStatsJson = JsonSerializer.Serialize(compacted.Stats, JsonOptions);
        session.LastCompactedAt = compacted.Stats.CompactedAt;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ChatMessage>> LoadMessagesAsync(
        string sessionId,
        bool preferCompactedSnapshot,
        bool includeReasoning,
        string? compatibilityGroup,
        CancellationToken cancellationToken)
    {
        if (preferCompactedSnapshot)
        {
            var snapshot = await dbContext.ChatSessions.AsNoTracking()
                .Where(session => session.SessionId == sessionId && !session.IsArchived)
                .Select(session => session.CompactedPromptSnapshotJson)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(snapshot))
            {
                var snapshotMessages = DeserializeMessages(snapshot);
                if (snapshotMessages.Count > 0)
                {
                    return snapshotMessages;
                }
            }
        }

        var storedMessages = await dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.Sequence)
            .Select(message => new
            {
                message.Id,
                message.Role,
                message.Content,
                message.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var messages = storedMessages.Select(message => new ChatMessage(ToChatRole(message.Role), message.Content)
        {
            MessageId = message.Id,
            CreatedAt = message.CreatedAt
        }).ToList();

        if (!includeReasoning)
        {
            return messages;
        }

        var traceQuery = dbContext.ReasoningTraces.AsNoTracking()
            .Where(trace => trace.SessionId == sessionId);
        if (!string.IsNullOrWhiteSpace(compatibilityGroup))
        {
            var sessionModelGroup = await dbContext.ChatSessions.AsNoTracking()
                .Where(session => session.SessionId == sessionId && session.ModelCompatibilityGroup == compatibilityGroup)
                .Select(session => session.ModelCompatibilityGroup)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(sessionModelGroup))
            {
                return messages;
            }
        }

        var traces = await traceQuery
            .OrderBy(trace => trace.TurnSequence)
            .ToListAsync(cancellationToken);
        var assistantMessages = messages
            .Where(message => message.Role == ChatRole.Assistant)
            .ToList();
        for (var index = 0; index < traces.Count && index < assistantMessages.Count; index++)
        {
            var reasoning = ExtractReasoningText(traces[index].ReasoningContentJson);
            if (!string.IsNullOrWhiteSpace(reasoning))
            {
                assistantMessages[index].Contents.Insert(0, new TextReasoningContent(reasoning));
            }
        }

        return messages;
    }

    private async Task<ContextPolicyDto> LoadStoredContextPolicyAsync(string sessionId, CancellationToken cancellationToken)
    {
        var policyJson = await dbContext.ChatSessions.AsNoTracking()
            .Where(session => session.SessionId == sessionId && !session.IsArchived)
            .Select(session => session.ContextPolicyJson)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(policyJson)
            ? new ContextPolicyDto { Enabled = true, Mode = ContextPolicyResolver.ModePersistedPrompt, Profile = ContextPolicyResolver.ProfileCheapNoLlm }
            : JsonSerializer.Deserialize<ContextPolicyDto>(policyJson, JsonOptions)
                ?? new ContextPolicyDto { Enabled = true, Mode = ContextPolicyResolver.ModePersistedPrompt, Profile = ContextPolicyResolver.ProfileCheapNoLlm };
    }

    private static string? TryGetSessionString(AgentSession session, string key)
        => session.StateBag.TryGetValue<string>(key, out var value) ? value : null;

    private static ContextPolicyDto? TryGetPolicy(AgentSession session)
    {
        if (!session.StateBag.TryGetValue<string>(ContextPolicyStateKey, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ContextPolicyDto>(json, JsonOptions);
    }

    private static ChatRole ToChatRole(string role)
        => role switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            "tool" => ChatRole.Tool,
            _ => ChatRole.User
        };

    private static string SerializeMessages(IReadOnlyList<ChatMessage> messages)
        => JsonSerializer.Serialize(messages, AIJsonUtilities.DefaultOptions);

    private static IReadOnlyList<ChatMessage> DeserializeMessages(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ExtractReasoningText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("text", out var text)
                ? text.GetString() ?? ""
                : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
