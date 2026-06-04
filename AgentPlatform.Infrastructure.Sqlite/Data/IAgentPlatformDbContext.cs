using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public interface IAgentPlatformDbContext
{
    DbSet<UserAgentEntity> UserAgents { get; }

    DbSet<ChatSessionEntity> ChatSessions { get; }

    DbSet<ChatMessageEntity> ChatMessages { get; }

    DbSet<RunEventEntity> RunEvents { get; }

    DbSet<ReasoningTraceEntity> ReasoningTraces { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
