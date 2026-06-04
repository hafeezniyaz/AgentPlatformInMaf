using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Stores;

public sealed class SqliteConversationStore(IAgentPlatformDbContext dbContext) : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<SessionSummaryDto>> ListSessionsAsync(
        string? agentId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = dbContext.ChatSessions.AsNoTracking().Where(session => !session.IsArchived);

        if (!string.IsNullOrWhiteSpace(agentId))
        {
            query = query.Where(session => session.AgentId == agentId);
        }

        if (DateTimeOffset.TryParse(cursor, out var cursorTimestamp))
        {
            query = query.Where(session => session.UpdatedAt < cursorTimestamp);
        }

        var sessions = await query
            .OrderByDescending(session => session.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var ids = sessions.Select(session => session.SessionId).ToList();
        var counts = await dbContext.ChatMessages.AsNoTracking()
            .Where(message => ids.Contains(message.SessionId))
            .GroupBy(message => message.SessionId)
            .Select(group => new { SessionId = group.Key, Count = group.Count(), Last = group.OrderByDescending(message => message.Sequence).First().Content })
            .ToDictionaryAsync(item => item.SessionId, cancellationToken);

        return sessions.Select(session =>
        {
            counts.TryGetValue(session.SessionId, out var messageInfo);
            return new SessionSummaryDto(
                session.SessionId,
                session.AgentId,
                session.AgentName,
                session.Title,
                session.Preview,
                messageInfo?.Last,
                session.CreatedAt,
                session.UpdatedAt,
                messageInfo?.Count ?? 0);
        }).ToList();
    }

    public async Task<SessionDetailsDto?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && !item.IsArchived, cancellationToken);

        if (session is null)
        {
            return null;
        }

        var messages = await GetMessageDtos(sessionId).ToListAsync(cancellationToken);

        return new SessionDetailsDto(
            session.SessionId,
            session.AgentId,
            session.AgentName,
            session.Title,
            session.Preview,
            JsonList.Read(session.ToolIdsJson),
            JsonList.Read(session.MiddlewareIdsJson),
            JsonList.Read(session.SkillIdsJson),
            ReadContextPolicy(session.ContextPolicyJson),
            session.ContextMode,
            session.ContextProfile,
            ReadCompactionStats(session.LastCompactionStatsJson),
            ReadThinkingPolicy(session.ThinkingPolicyJson),
            session.ThinkingMode,
            session.ThinkingCapture,
            session.ReasoningTraceCount,
            session.LastReasoningTokenEstimate,
            ReadModel(session),
            session.CreatedAt,
            session.UpdatedAt,
            messages);
    }

    public async Task<PagedMessagesDto?> GetMessagesAsync(string sessionId, int limit, int offset, CancellationToken cancellationToken)
    {
        var exists = await dbContext.ChatSessions.AnyAsync(session => session.SessionId == sessionId && !session.IsArchived, cancellationToken);
        if (!exists)
        {
            return null;
        }

        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Max(0, offset);
        var total = await dbContext.ChatMessages.CountAsync(message => message.SessionId == sessionId, cancellationToken);
        var messages = await GetMessageDtos(sessionId)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedMessagesDto(messages, limit, offset, total);
    }

    public async Task<bool> ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        session.IsArchived = true;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<StoredSession?> GetStoredSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && !item.IsArchived, cancellationToken);

        return session is null
            ? null
            : new StoredSession(
                session.SessionId,
                session.AgentId,
                session.AgentName,
                session.ConfigHash,
                session.SerializedSessionState,
                JsonList.Read(session.ToolIdsJson),
                JsonList.Read(session.MiddlewareIdsJson),
                JsonList.Read(session.SkillIdsJson),
                ReadContextPolicy(session.ContextPolicyJson),
                session.ContextMode,
                session.ContextProfile,
                session.CompactedPromptSnapshotJson,
                ReadCompactionStats(session.LastCompactionStatsJson),
                ReadThinkingPolicy(session.ThinkingPolicyJson),
                session.ThinkingMode,
                session.ThinkingCapture,
                session.ReasoningTraceCount,
                session.LastReasoningTokenEstimate,
                ReadResolvedModel(session),
                string.IsNullOrWhiteSpace(session.AgentStateJson) ? "{}" : session.AgentStateJson);
    }

    public async Task<StoredSession> CreateSessionAsync(
        string sessionId,
        string agentId,
        string agentName,
        string title,
        string configHash,
        IReadOnlyList<string> toolIds,
        IReadOnlyList<string> middlewareIds,
        IReadOnlyList<string> skillIds,
        ContextPolicyDto contextPolicy,
        ThinkingPolicyDto thinkingPolicy,
        ResolvedModel model,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new ChatSessionEntity
        {
            SessionId = sessionId,
            AgentId = agentId,
            AgentName = agentName,
            Title = title,
            Preview = title,
            ConfigHash = configHash,
            ToolIdsJson = JsonList.Write(toolIds),
            MiddlewareIdsJson = JsonList.Write(middlewareIds),
            SkillIdsJson = JsonList.Write(skillIds),
            ContextPolicyJson = WriteContextPolicy(contextPolicy),
            ContextMode = contextPolicy.Mode ?? "inFlight",
            ContextProfile = contextPolicy.Profile ?? "balanced",
            ThinkingPolicyJson = WriteThinkingPolicy(thinkingPolicy),
            ThinkingMode = thinkingPolicy.Mode ?? "disabled",
            ThinkingCapture = thinkingPolicy.Capture ?? "opaque",
            ModelId = model.Id,
            ModelProvider = model.Provider,
            ModelBaseUrl = model.BaseUrl,
            ModelCompatibilityGroup = model.CompatibilityGroup,
            ModelContextWindowTokens = model.ContextWindowTokens,
            AgentStateJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ChatSessions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new StoredSession(
            sessionId,
            agentId,
            agentName,
            configHash,
            null,
            toolIds,
            middlewareIds,
            skillIds,
            contextPolicy,
            contextPolicy.Mode ?? "inFlight",
            contextPolicy.Profile ?? "balanced",
            null,
            null,
            thinkingPolicy,
            thinkingPolicy.Mode ?? "disabled",
            thinkingPolicy.Capture ?? "opaque",
            0,
            0,
            model,
            "{}");
    }

    public async Task AddMessageAsync(string sessionId, string role, string content, CancellationToken cancellationToken)
    {
        var nextSequence = await dbContext.ChatMessages
            .Where(message => message.SessionId == sessionId)
            .Select(message => (int?)message.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        dbContext.ChatMessages.Add(new ChatMessageEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            SessionId = sessionId,
            Role = role,
            Content = content,
            Sequence = nextSequence + 1,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await TouchSession(sessionId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRunEventAsync(string sessionId, string eventName, object payload, CancellationToken cancellationToken)
    {
        dbContext.RunEvents.Add(new RunEventEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            SessionId = sessionId,
            EventName = eventName,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAgentStateAsync(string sessionId, string agentStateJson, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstAsync(item => item.SessionId == sessionId, cancellationToken);
        session.AgentStateJson = string.IsNullOrWhiteSpace(agentStateJson) ? "{}" : agentStateJson;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSessionConfigurationAsync(
        string sessionId,
        string configHash,
        ThinkingPolicyDto thinkingPolicy,
        ResolvedModel model,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstAsync(item => item.SessionId == sessionId, cancellationToken);
        session.ConfigHash = configHash;
        session.SerializedSessionState = null;
        session.ThinkingPolicyJson = WriteThinkingPolicy(thinkingPolicy);
        session.ThinkingMode = thinkingPolicy.Mode ?? "disabled";
        session.ThinkingCapture = thinkingPolicy.Capture ?? "opaque";
        session.ModelId = model.Id;
        session.ModelProvider = model.Provider;
        session.ModelBaseUrl = model.BaseUrl;
        session.ModelCompatibilityGroup = model.CompatibilityGroup;
        session.ModelContextWindowTokens = model.ContextWindowTokens;
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSessionAfterRunAsync(
        string sessionId,
        string preview,
        string? serializedSessionState,
        string? compactedPromptSnapshotJson,
        ContextCompactionStatsDto? compactionStats,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstAsync(item => item.SessionId == sessionId, cancellationToken);
        session.Preview = string.IsNullOrWhiteSpace(preview) ? session.Preview : preview;
        session.SerializedSessionState = serializedSessionState;
        if (!string.IsNullOrWhiteSpace(compactedPromptSnapshotJson))
        {
            session.CompactedPromptSnapshotJson = compactedPromptSnapshotJson;
        }

        if (compactionStats is not null)
        {
            session.LastCompactionStatsJson = JsonSerializer.Serialize(compactionStats, JsonOptions);
            session.LastCompactedAt = compactionStats.CompactedAt;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<ChatMessageDto> GetMessageDtos(string sessionId)
        => dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.Sequence)
            .Select(message => new ChatMessageDto(
                message.Id,
                message.Role,
                message.Content,
                message.Sequence,
                message.CreatedAt,
                null));

    private async Task TouchSession(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (session is not null)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static string WriteContextPolicy(ContextPolicyDto contextPolicy)
        => JsonSerializer.Serialize(contextPolicy, JsonOptions);

    private static ContextPolicyDto ReadContextPolicy(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new ContextPolicyDto { Enabled = true, Mode = "inFlight", Profile = "balanced", SummarizerModel = "gpt-4o-mini" }
            : JsonSerializer.Deserialize<ContextPolicyDto>(json, JsonOptions)
                ?? new ContextPolicyDto { Enabled = true, Mode = "inFlight", Profile = "balanced", SummarizerModel = "gpt-4o-mini" };

    private static ContextCompactionStatsDto? ReadCompactionStats(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ContextCompactionStatsDto>(json, JsonOptions);

    private static string WriteThinkingPolicy(ThinkingPolicyDto thinkingPolicy)
        => JsonSerializer.Serialize(thinkingPolicy, JsonOptions);

    private static ThinkingPolicyDto ReadThinkingPolicy(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? new ThinkingPolicyDto { Enabled = false, Mode = "disabled", Capture = "opaque", ExposeToClient = false, MaxPreservedTokens = 24000 }
            : JsonSerializer.Deserialize<ThinkingPolicyDto>(json, JsonOptions)
                ?? new ThinkingPolicyDto { Enabled = false, Mode = "disabled", Capture = "opaque", ExposeToClient = false, MaxPreservedTokens = 24000 };

    private static ModelDefinitionDto ReadModel(ChatSessionEntity session)
    {
        var resolved = ReadResolvedModel(session);
        return new ModelDefinitionDto(
            resolved.Id,
            resolved.Provider,
            resolved.BaseUrl,
            resolved.CompatibilityGroup,
            resolved.ContextWindowTokens,
            resolved.ThinkingPolicy);
    }

    private static ResolvedModel ReadResolvedModel(ChatSessionEntity session)
    {
        var modelId = string.IsNullOrWhiteSpace(session.ModelId) ? "unknown" : session.ModelId;
        return new ResolvedModel(
            modelId,
            string.IsNullOrWhiteSpace(session.ModelProvider) ? "openai" : session.ModelProvider,
            session.ModelBaseUrl,
            string.IsNullOrWhiteSpace(session.ModelCompatibilityGroup) ? modelId : session.ModelCompatibilityGroup,
            session.ModelContextWindowTokens,
            ReadThinkingPolicy(session.ThinkingPolicyJson));
    }
}
