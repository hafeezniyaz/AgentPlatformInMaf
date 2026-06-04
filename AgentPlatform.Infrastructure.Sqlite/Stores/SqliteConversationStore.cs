using System.Text.Json;
using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Stores;

public sealed class SqliteConversationStore(AgentPlatformDbContext dbContext) : IConversationStore
{
    private const string BranchRoot = "root";
    private const string BranchFork = "fork";
    private const string PendingStatus = "pending";
    private const string CancelledStatus = "cancelled";
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
            .Select(group => new
            {
                SessionId = group.Key,
                Count = group.Count(),
                Last = group.OrderByDescending(message => message.Sequence).First().Content
            })
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
                messageInfo?.Count ?? 0,
                session.ParentSessionId,
                session.ForkedFromMessageId,
                session.ForkedFromSequence,
                string.IsNullOrWhiteSpace(session.BranchKind) ? BranchRoot : session.BranchKind);
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
        return ToDetails(session, messages);
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

        return session is null ? null : ToStored(session);
    }

    public async Task<ChatMessageDto?> GetMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken)
        => await dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == sessionId && message.Id == messageId)
            .Select(message => new ChatMessageDto(
                message.Id,
                message.Role,
                message.Content,
                message.Sequence,
                message.CreatedAt,
                null))
            .FirstOrDefaultAsync(cancellationToken);

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
            BranchKind = BranchRoot,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ChatSessions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToStored(entity);
    }

    public async Task<ChatMessageDto> AddMessageAsync(
        string sessionId,
        string role,
        string content,
        CancellationToken cancellationToken,
        string? messageId = null)
    {
        var nextSequence = await dbContext.ChatMessages
            .Where(message => message.SessionId == sessionId)
            .Select(message => (int?)message.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var entity = new ChatMessageEntity
        {
            Id = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("n") : messageId,
            SessionId = sessionId,
            Role = role,
            Content = content,
            Sequence = nextSequence + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ChatMessages.Add(entity);
        await TouchSession(sessionId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }

    public async Task SavePreTurnCheckpointAsync(string sessionId, string messageId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && !item.IsArchived, cancellationToken)
            ?? throw new AgentPlatformValidationException("Session not found.");

        var sequence = (await dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .Select(message => (int?)message.Sequence)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var pendingRequests = await dbContext.PendingHumanRequests.AsNoTracking()
            .Where(request => request.SessionId == sessionId && request.Status == PendingStatus)
            .OrderBy(request => request.MessageSequence)
            .ThenBy(request => request.Id)
            .Select(request => new
            {
                request.Id,
                request.MessageId,
                request.MessageSequence,
                request.RunId,
                request.RequestType,
                request.PayloadJson,
                request.Status,
                request.CreatedAt,
                request.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var existing = await dbContext.MessageCheckpoints
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && item.MessageId == messageId, cancellationToken);
        var checkpoint = existing ?? new MessageCheckpointEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            SessionId = sessionId,
            MessageId = messageId
        };

        checkpoint.Sequence = sequence;
        checkpoint.SerializedSessionState = session.SerializedSessionState;
        checkpoint.AgentStateJson = string.IsNullOrWhiteSpace(session.AgentStateJson) ? "{}" : session.AgentStateJson;
        checkpoint.CompactedPromptSnapshotJson = session.CompactedPromptSnapshotJson;
        checkpoint.LastCompactionStatsJson = session.LastCompactionStatsJson;
        checkpoint.ReasoningTraceCount = session.ReasoningTraceCount;
        checkpoint.LastReasoningTokenEstimate = session.LastReasoningTokenEstimate;
        checkpoint.PendingHumanRequestsJson = JsonSerializer.Serialize(pendingRequests, JsonOptions);
        checkpoint.CreatedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            dbContext.MessageCheckpoints.Add(checkpoint);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<RetryTurnDto> PrepareRetryAsync(
        string sessionId,
        string messageId,
        CancellationToken cancellationToken,
        string? updatedMessage = null)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var session = await dbContext.ChatSessions
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && !item.IsArchived, cancellationToken)
            ?? throw new AgentPlatformValidationException("Session not found.");
        var message = await dbContext.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && item.Id == messageId, cancellationToken)
            ?? throw new AgentPlatformValidationException("Message not found.");

        if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentPlatformValidationException("Only user messages can be retried.");
        }

        var checkpoint = await dbContext.MessageCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && item.MessageId == messageId, cancellationToken)
            ?? throw new AgentPlatformConflictException("The selected message does not have a restorable checkpoint.");

        await RestoreCheckpointAsync(session, checkpoint, cancellationToken);
        dbContext.ChatMessages.RemoveRange(await dbContext.ChatMessages
            .Where(item => item.SessionId == sessionId && item.Sequence >= checkpoint.Sequence)
            .ToListAsync(cancellationToken));
        dbContext.MessageCheckpoints.RemoveRange(await dbContext.MessageCheckpoints
            .Where(item => item.SessionId == sessionId && item.Sequence >= checkpoint.Sequence)
            .ToListAsync(cancellationToken));
        dbContext.ReasoningTraces.RemoveRange(await dbContext.ReasoningTraces
            .Where(trace => trace.SessionId == sessionId && trace.TurnSequence > checkpoint.ReasoningTraceCount)
            .ToListAsync(cancellationToken));
        var laterRunEvents = (await dbContext.RunEvents
            .Where(runEvent => runEvent.SessionId == sessionId)
            .ToListAsync(cancellationToken))
            .Where(runEvent => runEvent.CreatedAt >= checkpoint.CreatedAt)
            .ToList();
        dbContext.RunEvents.RemoveRange(laterRunEvents);

        await CancelPendingHumanRequestsFromSequenceAsync(sessionId, checkpoint.Sequence, cancellationToken);
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RetryTurnDto(
            sessionId,
            messageId,
            message.Sequence,
            session.AgentId,
            updatedMessage ?? message.Content,
            session.ModelId,
            message.Content);
    }

    public async Task<SessionDetailsDto> ForkSessionAsync(
        string sessionId,
        string messageId,
        string? title,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var source = await dbContext.ChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && !item.IsArchived, cancellationToken)
            ?? throw new AgentPlatformValidationException("Session not found.");
        var targetMessage = await dbContext.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && item.Id == messageId, cancellationToken)
            ?? throw new AgentPlatformValidationException("Message not found.");

        if (!string.Equals(targetMessage.Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentPlatformValidationException("Only user messages can be forked.");
        }

        var checkpoint = await dbContext.MessageCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(item => item.SessionId == sessionId && item.MessageId == messageId, cancellationToken)
            ?? throw new AgentPlatformConflictException("The selected message does not have a restorable checkpoint.");

        var now = DateTimeOffset.UtcNow;
        var forkSessionId = Guid.NewGuid().ToString("n");
        var fork = new ChatSessionEntity
        {
            SessionId = forkSessionId,
            AgentId = source.AgentId,
            AgentName = source.AgentName,
            Title = string.IsNullOrWhiteSpace(title) ? $"Fork of {source.Title}" : title.Trim(),
            Preview = source.Preview,
            ConfigHash = source.ConfigHash,
            ToolIdsJson = source.ToolIdsJson,
            MiddlewareIdsJson = source.MiddlewareIdsJson,
            SkillIdsJson = source.SkillIdsJson,
            SerializedSessionState = checkpoint.SerializedSessionState,
            AgentStateJson = string.IsNullOrWhiteSpace(checkpoint.AgentStateJson) ? "{}" : checkpoint.AgentStateJson,
            ContextPolicyJson = source.ContextPolicyJson,
            ContextMode = source.ContextMode,
            ContextProfile = source.ContextProfile,
            CompactedPromptSnapshotJson = checkpoint.CompactedPromptSnapshotJson,
            LastCompactionStatsJson = checkpoint.LastCompactionStatsJson,
            ThinkingPolicyJson = source.ThinkingPolicyJson,
            ThinkingMode = source.ThinkingMode,
            ThinkingCapture = source.ThinkingCapture,
            ReasoningTraceCount = checkpoint.ReasoningTraceCount,
            LastReasoningTokenEstimate = checkpoint.LastReasoningTokenEstimate,
            ModelId = source.ModelId,
            ModelProvider = source.ModelProvider,
            ModelBaseUrl = source.ModelBaseUrl,
            ModelCompatibilityGroup = source.ModelCompatibilityGroup,
            ModelContextWindowTokens = source.ModelContextWindowTokens,
            ParentSessionId = source.SessionId,
            ForkedFromMessageId = messageId,
            ForkedFromSequence = targetMessage.Sequence,
            BranchKind = BranchFork,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.ChatSessions.Add(fork);

        var messageIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceMessages = await dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.SessionId == sessionId && message.Sequence < checkpoint.Sequence)
            .OrderBy(message => message.Sequence)
            .ToListAsync(cancellationToken);
        foreach (var sourceMessage in sourceMessages)
        {
            var copiedId = Guid.NewGuid().ToString("n");
            messageIdMap[sourceMessage.Id] = copiedId;
            dbContext.ChatMessages.Add(new ChatMessageEntity
            {
                Id = copiedId,
                SessionId = forkSessionId,
                Role = sourceMessage.Role,
                Content = sourceMessage.Content,
                Sequence = sourceMessage.Sequence,
                MetadataJson = sourceMessage.MetadataJson,
                CreatedAt = sourceMessage.CreatedAt
            });
        }

        var sourceCheckpoints = await dbContext.MessageCheckpoints.AsNoTracking()
            .Where(item => item.SessionId == sessionId && item.Sequence < checkpoint.Sequence)
            .ToListAsync(cancellationToken);
        foreach (var sourceCheckpoint in sourceCheckpoints)
        {
            if (!messageIdMap.TryGetValue(sourceCheckpoint.MessageId, out var copiedMessageId))
            {
                continue;
            }

            dbContext.MessageCheckpoints.Add(new MessageCheckpointEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                SessionId = forkSessionId,
                MessageId = copiedMessageId,
                Sequence = sourceCheckpoint.Sequence,
                SerializedSessionState = sourceCheckpoint.SerializedSessionState,
                AgentStateJson = sourceCheckpoint.AgentStateJson,
                CompactedPromptSnapshotJson = sourceCheckpoint.CompactedPromptSnapshotJson,
                LastCompactionStatsJson = sourceCheckpoint.LastCompactionStatsJson,
                ReasoningTraceCount = sourceCheckpoint.ReasoningTraceCount,
                LastReasoningTokenEstimate = sourceCheckpoint.LastReasoningTokenEstimate,
                PendingHumanRequestsJson = sourceCheckpoint.PendingHumanRequestsJson,
                CreatedAt = sourceCheckpoint.CreatedAt
            });
        }

        var sourceTraces = await dbContext.ReasoningTraces.AsNoTracking()
            .Where(trace => trace.SessionId == sessionId && trace.TurnSequence <= checkpoint.ReasoningTraceCount)
            .OrderBy(trace => trace.TurnSequence)
            .ToListAsync(cancellationToken);
        foreach (var trace in sourceTraces)
        {
            dbContext.ReasoningTraces.Add(new ReasoningTraceEntity
            {
                Id = Guid.NewGuid().ToString("n"),
                SessionId = forkSessionId,
                MessageId = trace.MessageId is not null && messageIdMap.TryGetValue(trace.MessageId, out var copiedTraceMessageId)
                    ? copiedTraceMessageId
                    : null,
                TurnSequence = trace.TurnSequence,
                Role = trace.Role,
                Model = trace.Model,
                ReasoningContentJson = trace.ReasoningContentJson,
                TokenEstimate = trace.TokenEstimate,
                CaptureMode = trace.CaptureMode,
                CreatedAt = trace.CreatedAt
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var messages = await GetMessageDtos(forkSessionId).ToListAsync(cancellationToken);
        return ToDetails(fork, messages);
    }

    public async Task<PendingHumanRequestDto> AddPendingHumanRequestAsync(
        PendingHumanRequestWriteDto request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new PendingHumanRequestEntity
        {
            Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id,
            SessionId = request.SessionId,
            MessageId = request.MessageId,
            MessageSequence = request.MessageSequence,
            RunId = request.RunId,
            RequestType = request.RequestType,
            PayloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson,
            Status = PendingStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.PendingHumanRequests.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
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

    private async Task RestoreCheckpointAsync(
        ChatSessionEntity session,
        MessageCheckpointEntity checkpoint,
        CancellationToken cancellationToken)
    {
        session.SerializedSessionState = checkpoint.SerializedSessionState;
        session.AgentStateJson = string.IsNullOrWhiteSpace(checkpoint.AgentStateJson) ? "{}" : checkpoint.AgentStateJson;
        session.CompactedPromptSnapshotJson = checkpoint.CompactedPromptSnapshotJson;
        session.LastCompactionStatsJson = checkpoint.LastCompactionStatsJson;
        session.LastCompactedAt = ReadCompactionStats(checkpoint.LastCompactionStatsJson)?.CompactedAt;
        session.ReasoningTraceCount = checkpoint.ReasoningTraceCount;
        session.LastReasoningTokenEstimate = checkpoint.LastReasoningTokenEstimate;

        var restoredRequests = ReadCheckpointPendingRequests(checkpoint.PendingHumanRequestsJson)
            .Where(request => request.Status == PendingStatus)
            .ToList();
        foreach (var restored in restoredRequests)
        {
            var existing = await dbContext.PendingHumanRequests
                .FirstOrDefaultAsync(request => request.Id == restored.Id, cancellationToken);
            if (existing is not null)
            {
                existing.Status = PendingStatus;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                continue;
            }

            dbContext.PendingHumanRequests.Add(new PendingHumanRequestEntity
            {
                Id = restored.Id,
                SessionId = session.SessionId,
                MessageId = restored.MessageId,
                MessageSequence = restored.MessageSequence,
                RunId = restored.RunId,
                RequestType = restored.RequestType,
                PayloadJson = restored.PayloadJson,
                Status = PendingStatus,
                CreatedAt = restored.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task CancelPendingHumanRequestsFromSequenceAsync(
        string sessionId,
        int sequence,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.PendingHumanRequests
            .Where(request =>
                request.SessionId == sessionId &&
                request.Status == PendingStatus &&
                request.MessageSequence >= sequence)
            .ToListAsync(cancellationToken);

        foreach (var request in pending)
        {
            request.Status = CancelledStatus;
            request.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task TouchSession(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (session is not null)
        {
            session.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static StoredSession ToStored(ChatSessionEntity session)
        => new(
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

    private static SessionDetailsDto ToDetails(ChatSessionEntity session, IReadOnlyList<ChatMessageDto> messages)
        => new(
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
            messages,
            session.ParentSessionId,
            session.ForkedFromMessageId,
            session.ForkedFromSequence,
            string.IsNullOrWhiteSpace(session.BranchKind) ? BranchRoot : session.BranchKind);

    private static ChatMessageDto ToDto(ChatMessageEntity message)
        => new(
            message.Id,
            message.Role,
            message.Content,
            message.Sequence,
            message.CreatedAt,
            null);

    private static PendingHumanRequestDto ToDto(PendingHumanRequestEntity request)
        => new(
            request.Id,
            request.SessionId,
            request.MessageId,
            request.MessageSequence,
            request.RunId,
            request.RequestType,
            request.PayloadJson,
            request.Status,
            request.CreatedAt,
            request.UpdatedAt);

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

    private static IReadOnlyList<PendingHumanRequestDto> ReadCheckpointPendingRequests(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PendingHumanRequestDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
