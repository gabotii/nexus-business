using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Domain.Audit;
using Nexus.Infrastructure.Email;
using Nexus.Infrastructure.Identity;

namespace Nexus.Infrastructure.Persistence.Configurations;

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_sessions");
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.KeyHash).HasMaxLength(32).IsRequired();
        b.HasIndex(s => s.KeyHash).IsUnique().HasDatabaseName("ux_user_sessions_key_hash");
        b.HasIndex(s => new { s.UserId, s.RevokedAt });
        b.Property(s => s.Ticket).IsRequired();
        b.Property(s => s.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);
        b.Property(s => s.IpPrefix).HasMaxLength(64);
        b.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.ToTable("email_outbox");
        b.Property(m => m.Id).ValueGeneratedNever();
        // "to" is a reserved word in SQL.
        b.Property(m => m.To).HasColumnName("recipient").HasMaxLength(254).IsRequired();
        b.Property(m => m.Subject).HasMaxLength(200).IsRequired();
        b.Property(m => m.LastError).HasMaxLength(500);
        b.HasIndex(m => m.NextAttemptAt).HasFilter("sent_at IS NULL AND failed_at IS NULL")
            .HasDatabaseName("ix_email_outbox_due");
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("audit_log");
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.ActorType).HasConversion<string>().HasMaxLength(16);
        b.Property(a => a.Action).HasMaxLength(64).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(32);
        b.Property(a => a.EntityId).HasMaxLength(64);
        b.Property(a => a.CorrelationId).HasMaxLength(64);
        b.Property(a => a.IpPrefix).HasMaxLength(64);
        b.Property(a => a.Metadata).HasColumnType("jsonb");
        b.HasIndex(a => new { a.TenantId, a.OccurredAt });
        b.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
    }
}
