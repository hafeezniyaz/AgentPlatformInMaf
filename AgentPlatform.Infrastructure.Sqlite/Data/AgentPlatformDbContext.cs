using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public sealed class AgentPlatformDbContext(DbContextOptions<AgentPlatformDbContext> options) :
    DbContext(options),
    IAgentPlatformDbContext
{
    public DbSet<UserAgentEntity> UserAgents => Set<UserAgentEntity>();

    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();

    public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyAgentPlatformModel();
}
