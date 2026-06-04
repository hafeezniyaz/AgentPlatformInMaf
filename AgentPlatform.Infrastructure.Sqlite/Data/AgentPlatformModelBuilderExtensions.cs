using AgentPlatform.Infrastructure.Sqlite.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Sqlite.Data;

public static class AgentPlatformModelBuilderExtensions
{
    public static ModelBuilder ApplyAgentPlatformModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAgentEntity>(entity =>
        {
            entity.HasKey(agent => agent.Id);
            entity.Property(agent => agent.Id).HasMaxLength(128).ValueGeneratedNever();
            entity.Property(agent => agent.Name).HasMaxLength(200).IsRequired();
            entity.Property(agent => agent.Description).IsRequired();
            entity.Property(agent => agent.Source).HasMaxLength(32).IsRequired();
            entity.Property(agent => agent.Instructions).IsRequired();
            entity.Property(agent => agent.Model).IsRequired();
            entity.Property(agent => agent.ToolIdsJson).IsRequired();
            entity.Property(agent => agent.MiddlewareIdsJson).IsRequired();
            entity.Property(agent => agent.SkillIdsJson).IsRequired();
        });

        modelBuilder.Entity<ChatSessionEntity>(entity =>
        {
            entity.HasKey(session => session.SessionId);
            entity.Property(session => session.SessionId).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(session => session.AgentId).HasMaxLength(128).IsRequired();
            entity.Property(session => session.AgentName).IsRequired();
            entity.Property(session => session.Title).IsRequired();
            entity.Property(session => session.Preview).IsRequired();
            entity.Property(session => session.ConfigHash).HasMaxLength(128).IsRequired();
            entity.Property(session => session.ToolIdsJson).IsRequired();
            entity.Property(session => session.MiddlewareIdsJson).IsRequired();
            entity.Property(session => session.SkillIdsJson).IsRequired();
            entity.Property(session => session.AgentStateJson).HasDefaultValue("{}").IsRequired();
            entity.Property(session => session.ContextPolicyJson).IsRequired();
            entity.Property(session => session.ContextMode).HasMaxLength(40).IsRequired();
            entity.Property(session => session.ContextProfile).HasMaxLength(40).IsRequired();
            entity.Property(session => session.ThinkingPolicyJson).IsRequired();
            entity.Property(session => session.ThinkingMode).HasMaxLength(40).IsRequired();
            entity.Property(session => session.ThinkingCapture).HasMaxLength(40).IsRequired();
            entity.Property(session => session.ModelId).HasMaxLength(160).IsRequired();
            entity.Property(session => session.ModelProvider).HasMaxLength(40).IsRequired();
            entity.Property(session => session.ModelCompatibilityGroup).HasMaxLength(160).IsRequired();
            entity.HasIndex(session => new { session.IsArchived, session.UpdatedAt });
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(message => message.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(message => message.Role).HasMaxLength(32).IsRequired();
            entity.Property(message => message.Content).IsRequired();
            entity.HasIndex(message => new { message.SessionId, message.Sequence }).IsUnique();
            entity.HasOne<ChatSessionEntity>()
                .WithMany()
                .HasForeignKey(message => message.SessionId)
                .HasPrincipalKey(session => session.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RunEventEntity>(entity =>
        {
            entity.HasKey(runEvent => runEvent.Id);
            entity.Property(runEvent => runEvent.Id).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(runEvent => runEvent.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(runEvent => runEvent.EventName).HasMaxLength(80).IsRequired();
            entity.Property(runEvent => runEvent.PayloadJson).IsRequired();
            entity.HasIndex(runEvent => new { runEvent.SessionId, runEvent.CreatedAt });
            entity.HasOne<ChatSessionEntity>()
                .WithMany()
                .HasForeignKey(runEvent => runEvent.SessionId)
                .HasPrincipalKey(session => session.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReasoningTraceEntity>(entity =>
        {
            entity.HasKey(trace => trace.Id);
            entity.Property(trace => trace.Id).HasMaxLength(64).ValueGeneratedNever();
            entity.Property(trace => trace.SessionId).HasMaxLength(64).IsRequired();
            entity.Property(trace => trace.MessageId).HasMaxLength(64);
            entity.Property(trace => trace.Role).HasMaxLength(32).IsRequired();
            entity.Property(trace => trace.Model).HasMaxLength(160).IsRequired();
            entity.Property(trace => trace.ReasoningContentJson).IsRequired();
            entity.Property(trace => trace.CaptureMode).HasMaxLength(40).IsRequired();
            entity.HasIndex(trace => new { trace.SessionId, trace.TurnSequence }).IsUnique();
            entity.HasIndex(trace => new { trace.SessionId, trace.CreatedAt });
            entity.HasOne<ChatSessionEntity>()
                .WithMany()
                .HasForeignKey(trace => trace.SessionId)
                .HasPrincipalKey(session => session.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return modelBuilder;
    }
}
