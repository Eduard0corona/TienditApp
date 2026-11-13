using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        // Fase 31L (ADR 0092): a message brings no media, or a photo or audio with its id; saved, it has its path, and only then.
        builder.ToTable("WhatsAppMessages", t => t.HasCheckConstraint(
            "CK_WhatsAppMessages_Media",
            "([MediaKind] IS NULL AND [MediaId] IS NULL AND [MediaPath] IS NULL AND [MediaState] IS NULL) "
            + "OR ([MediaKind] IN (1, 2) AND [MediaId] IS NOT NULL "
            + "AND (([MediaState] IN (0, 2) AND [MediaPath] IS NULL) OR ([MediaState] = 1 AND [MediaPath] IS NOT NULL)))"));
        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId).IsRequired();
        builder.Property(m => m.Direction).HasConversion<int>().IsRequired();
        builder.Property(m => m.Body).HasMaxLength(4096).IsRequired();
        builder.Property(m => m.Status).HasConversion<int>().IsRequired();
        builder.Property(m => m.ExternalMessageId).HasMaxLength(200).IsUnicode(false);
        builder.Property(m => m.SentAt).IsRequired();
        // Fase 31L (ADR 0092): the photo or audio of a customer, downloaded in the background; null for everything else.
        builder.Property(m => m.MediaKind).HasConversion<int?>();
        builder.Property(m => m.MediaId).HasMaxLength(WhatsAppMessage.MaxMediaIdLength).IsUnicode(false);
        builder.Property(m => m.MediaPath).HasMaxLength(WhatsAppMessage.MaxMediaPathLength).IsUnicode(false);
        builder.Property(m => m.MediaState).HasConversion<int?>();

        builder.HasIndex(m => new { m.ConversationId, m.SentAt });
        // Idempotency key for webhook deliveries (Meta retries and may deliver twice).
        // Outbound messages have no external id yet, hence the filter.
        builder.HasIndex(m => m.ExternalMessageId).IsUnique().HasFilter("[ExternalMessageId] IS NOT NULL");
    }
}
