using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public sealed class AgentPlatformDbContext(DbContextOptions<AgentPlatformDbContext> options) : DbContext(options)
{
    public DbSet<UserAgentEntity> UserAgents => Set<UserAgentEntity>();

    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

    public DbSet<RunEventEntity> RunEvents => Set<RunEventEntity>();

    public DbSet<ReasoningTraceEntity> ReasoningTraces => Set<ReasoningTraceEntity>();

    public DbSet<MessageCheckpointEntity> MessageCheckpoints => Set<MessageCheckpointEntity>();

    public DbSet<PendingHumanRequestEntity> PendingHumanRequests => Set<PendingHumanRequestEntity>();

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
            entity.Property(session => session.AgentStateJson).HasDefaultValue("{}");
            entity.Property(session => session.ContextMode).HasMaxLength(40);
            entity.Property(session => session.ContextProfile).HasMaxLength(40);
            entity.Property(session => session.ThinkingMode).HasMaxLength(40);
            entity.Property(session => session.ThinkingCapture).HasMaxLength(40);
            entity.Property(session => session.ModelId).HasMaxLength(160);
            entity.Property(session => session.ModelProvider).HasMaxLength(40);
            entity.Property(session => session.ModelCompatibilityGroup).HasMaxLength(160);
            entity.Property(session => session.ParentSessionId).HasMaxLength(64);
            entity.Property(session => session.ForkedFromMessageId).HasMaxLength(64);
            entity.Property(session => session.BranchKind).HasMaxLength(40);
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

        modelBuilder.Entity<ReasoningTraceEntity>(entity =>
        {
            entity.HasKey(trace => trace.Id);
            entity.Property(trace => trace.SessionId).HasMaxLength(64);
            entity.Property(trace => trace.MessageId).HasMaxLength(64);
            entity.Property(trace => trace.Role).HasMaxLength(32);
            entity.Property(trace => trace.Model).HasMaxLength(160);
            entity.Property(trace => trace.CaptureMode).HasMaxLength(40);
            entity.HasIndex(trace => new { trace.SessionId, trace.TurnSequence });
            entity.HasIndex(trace => new { trace.SessionId, trace.CreatedAt });
        });

        modelBuilder.Entity<MessageCheckpointEntity>(entity =>
        {
            entity.HasKey(checkpoint => checkpoint.Id);
            entity.Property(checkpoint => checkpoint.Id).HasMaxLength(64);
            entity.Property(checkpoint => checkpoint.SessionId).HasMaxLength(64);
            entity.Property(checkpoint => checkpoint.MessageId).HasMaxLength(64);
            entity.Property(checkpoint => checkpoint.AgentStateJson).HasDefaultValue("{}");
            entity.Property(checkpoint => checkpoint.PendingHumanRequestsJson).HasDefaultValue("[]");
            entity.HasIndex(checkpoint => new { checkpoint.SessionId, checkpoint.MessageId }).IsUnique();
            entity.HasIndex(checkpoint => new { checkpoint.SessionId, checkpoint.Sequence });
        });

        modelBuilder.Entity<PendingHumanRequestEntity>(entity =>
        {
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).HasMaxLength(64);
            entity.Property(request => request.SessionId).HasMaxLength(64);
            entity.Property(request => request.MessageId).HasMaxLength(64);
            entity.Property(request => request.RunId).HasMaxLength(64);
            entity.Property(request => request.RequestType).HasMaxLength(120);
            entity.Property(request => request.Status).HasMaxLength(40);
            entity.HasIndex(request => new { request.SessionId, request.Status });
            entity.HasIndex(request => new { request.SessionId, request.MessageSequence });
        });
    }
}
