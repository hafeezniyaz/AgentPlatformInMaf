using AgentPlatform.Core.Models;
using AgentPlatform.Core.Services;
using AgentPlatform.Infrastructure.Sqlite.Data;
using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Stores;

public sealed class SqliteReasoningTraceStore(IAgentPlatformDbContext dbContext) : IReasoningTraceStore
{
    public async Task<IReadOnlyList<ReasoningTraceDto>> GetForSessionAsync(
        string sessionId,
        string? compatibilityGroup,
        int limit,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 500);
        var query = dbContext.ReasoningTraces.AsNoTracking()
            .Where(trace => trace.SessionId == sessionId);

        if (!string.IsNullOrWhiteSpace(compatibilityGroup))
        {
            var compatibleModels = dbContext.ChatSessions.AsNoTracking()
                .Where(session => session.SessionId == sessionId && session.ModelCompatibilityGroup == compatibilityGroup)
                .Select(session => session.ModelId);
            query = query.Where(trace => compatibleModels.Contains(trace.Model));
        }

        var traces = await query
            .OrderByDescending(trace => trace.TurnSequence)
            .Take(limit)
            .OrderBy(trace => trace.TurnSequence)
            .ToListAsync(cancellationToken);

        return traces.Select(ToDto).ToList();
    }

    public async Task SaveAsync(ReasoningTraceWriteDto trace, CancellationToken cancellationToken)
    {
        dbContext.ReasoningTraces.Add(new ReasoningTraceEntity
        {
            Id = Guid.NewGuid().ToString("n"),
            SessionId = trace.SessionId,
            MessageId = trace.MessageId,
            TurnSequence = trace.TurnSequence,
            Role = trace.Role,
            Model = trace.Model,
            ReasoningContentJson = trace.ReasoningContentJson,
            TokenEstimate = trace.TokenEstimate,
            CaptureMode = trace.CaptureMode,
            CreatedAt = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await UpdateSessionStatsAsync(trace.SessionId, cancellationToken);
    }

    public async Task PruneOrCompactAsync(string sessionId, ThinkingPolicyDto policy, CancellationToken cancellationToken)
    {
        var maxTokens = policy.MaxPreservedTokens ?? 24000;
        var traces = await dbContext.ReasoningTraces
            .Where(trace => trace.SessionId == sessionId)
            .OrderByDescending(trace => trace.TurnSequence)
            .ToListAsync(cancellationToken);

        var total = 0;
        var remove = new List<ReasoningTraceEntity>();
        foreach (var trace in traces)
        {
            total += Math.Max(0, trace.TokenEstimate);
            if (total > maxTokens)
            {
                remove.Add(trace);
            }
        }

        if (remove.Count > 0)
        {
            dbContext.ReasoningTraces.RemoveRange(remove);
            await dbContext.SaveChangesAsync(cancellationToken);
            await UpdateSessionStatsAsync(sessionId, cancellationToken);
        }
    }

    private async Task UpdateSessionStatsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.ChatSessions.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        var traces = await dbContext.ReasoningTraces.AsNoTracking()
            .Where(trace => trace.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        session.ReasoningTraceCount = traces.Count;
        session.LastReasoningTokenEstimate = traces
            .OrderByDescending(trace => trace.TurnSequence)
            .Select(trace => trace.TokenEstimate)
            .FirstOrDefault();
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ReasoningTraceDto ToDto(ReasoningTraceEntity entity)
        => new(
            entity.Id,
            entity.SessionId,
            entity.MessageId,
            entity.TurnSequence,
            entity.Role,
            entity.Model,
            entity.ReasoningContentJson,
            entity.TokenEstimate,
            entity.CaptureMode,
            entity.CreatedAt);
}
