using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;
using FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(300).IsUnicode(false).IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.ActorUserId).HasMaxLength(100);
        builder.Property(m => m.ActorName).HasMaxLength(256);
        QueueItemMapping.Configure(builder, "OutboxMessages");
    }
}

public class OutboxMessageConsumerConfiguration : IEntityTypeConfiguration<OutboxMessageConsumer>
{
    public void Configure(EntityTypeBuilder<OutboxMessageConsumer> builder)
    {
        builder.ToTable("OutboxMessageConsumers");
        builder.HasKey(c => new { c.MessageId, c.HandlerName });
        builder.Property(c => c.HandlerName).HasMaxLength(300).IsUnicode(false);
        builder.HasOne<OutboxMessage>()
            .WithMany()
            .HasForeignKey(c => c.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("InboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Source).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(m => m.PayloadHash).HasMaxLength(64).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(m => m.Payload).IsRequired();
        builder.HasIndex(m => new { m.Source, m.PayloadHash }).IsUnique();
        QueueItemMapping.Configure(builder, "InboxMessages");
    }
}

internal static class QueueItemMapping
{
    public static void Configure<T>(EntityTypeBuilder<T> builder, string table)
        where T : QueueItem
    {
        builder.Property(m => m.LastError).HasMaxLength(1000);
        // Renewed on every claim: the update that claims an item fails if another worker claimed it first.
        builder.Property(m => m.LockToken).IsConcurrencyToken();
        // Pending items, in the order workers look for them.
        builder.HasIndex(m => new { m.NextAttemptAt, m.CreatedAt })
            .HasDatabaseName($"IX_{table}_Pending")
            .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
        builder.HasIndex(m => m.ProcessedAt)
            .HasDatabaseName($"IX_{table}_ProcessedAt")
            .HasFilter("[ProcessedAt] IS NOT NULL");
    }
}
