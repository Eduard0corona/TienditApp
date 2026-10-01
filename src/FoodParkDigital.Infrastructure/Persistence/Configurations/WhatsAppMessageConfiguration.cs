using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.ToTable("WhatsAppMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.Direction).HasConversion<int>().IsRequired();
        builder.Property(m => m.Body).HasMaxLength(4096).IsRequired();
        builder.Property(m => m.Status).HasConversion<int>().IsRequired();
        builder.Property(m => m.ExternalMessageId).HasMaxLength(200).IsUnicode(false);
        builder.Property(m => m.SentAt).IsRequired();

        builder.HasIndex(m => new { m.ConversationId, m.SentAt });
        // Idempotency key for webhook deliveries (Meta retries and may deliver twice).
        // Outbound messages have no external id yet, hence the filter.
        builder.HasIndex(m => m.ExternalMessageId).IsUnique().HasFilter("[ExternalMessageId] IS NOT NULL");
    }
}
