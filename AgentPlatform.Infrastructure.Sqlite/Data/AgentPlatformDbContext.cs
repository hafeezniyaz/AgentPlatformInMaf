using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public sealed class AgentPlatformDbContext(DbContextOptions<AgentPlatformDbContext> options) : DbContext(options)
{
    public DbSet<UserAgentEntity> UserAgents => Set<UserAgentEntity>();

    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAgentEntity>(entity =>
        {
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.Id).HasMaxLength(128);
            entity.Property(agent => agent.Name).HasMaxLength(200);
            entity.Property(agent => agent.Source).HasMaxLength(32);
        });

        modelBuilder.Entity<ChatSessionEntity>(entity =>
        {
            entity.HasKey(session => session.SessionId);
            entity.Property(session => session.SessionId).HasMaxLength(64);
            entity.Property(session => session.AgentId).HasMaxLength(128);
            entity.Property(session => session.ConfigHash).HasMaxLength(128);
            entity.Property(session => session.ContextMode).HasMaxLength(40);
            entity.Property(session => session.ContextProfile).HasMaxLength(40);
            entity.HasIndex(session => new { session.IsArchived, session.UpdatedAt });
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.SessionId).HasMaxLength(64);
            entity.Property(message => message.Role).HasMaxLength(32);
            entity.HasIndex(message => new { message.SessionId, message.Sequence });
        });

        modelBuilder.Entity<RunEventEntity>(entity =>
        {
            entity.HasKey(runEvent => runEvent.Id);
            entity.Property(runEvent => runEvent.SessionId).HasMaxLength(64);
            entity.Property(runEvent => runEvent.EventName).HasMaxLength(80);
            entity.HasIndex(runEvent => new { runEvent.SessionId, runEvent.CreatedAt });
        });
    }
}
