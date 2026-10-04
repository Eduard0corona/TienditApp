using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("MessageTemplates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(MessageTemplate.MaxNameLength).IsRequired();
        builder.Property(x => x.Purpose).HasConversion<int>().IsRequired();
        builder.Property(x => x.Body).HasMaxLength(MessageTemplate.MaxBodyLength).IsRequired();
        builder.Property(x => x.ProviderTemplateName).HasMaxLength(MessageTemplate.MaxProviderTemplateNameLength).IsUnicode(false);
        builder.Property(x => x.LanguageCode).HasMaxLength(MessageTemplate.MaxLanguageCodeLength).IsUnicode(false).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Purpose, x.IsActive });
    }
}

public class MessageCampaignConfiguration : IEntityTypeConfiguration<MessageCampaign>
{
    public void Configure(EntityTypeBuilder<MessageCampaign> builder)
    {
        builder.ToTable("MessageCampaigns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(MessageCampaign.MaxNameLength).IsRequired();
        builder.Property(x => x.AudienceKind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.LockToken).IsConcurrencyToken();

        // A campaign keeps the template it was sent with: the template cannot be deleted while it is used.
        builder.HasOne<MessageTemplate>()
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        // The worker looks for due campaigns across businesses.
        builder.HasIndex(x => new { x.Status, x.ScheduledAt })
            .HasDatabaseName("IX_MessageCampaigns_Due");
    }
}

public class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessage>
{
    public void Configure(EntityTypeBuilder<OutgoingMessage> builder)
    {
        builder.ToTable("OutgoingMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Purpose).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(MessagingPhone.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.RecipientName).HasMaxLength(OutgoingMessage.MaxRecipientNameLength);
        builder.Property(x => x.Body).HasMaxLength(OutgoingMessage.MaxBodyLength).IsRequired();
        builder.Property(x => x.ProviderTemplateName).HasMaxLength(MessageTemplate.MaxProviderTemplateNameLength).IsUnicode(false);
        builder.Property(x => x.LanguageCode).HasMaxLength(MessageTemplate.MaxLanguageCodeLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.ParametersJson).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(OutgoingMessage.MaxProviderMessageIdLength).IsUnicode(false);
        builder.Property(x => x.LastError).HasMaxLength(OutgoingMessage.MaxErrorLength);
        builder.Property(x => x.DeduplicationKey).HasMaxLength(OutgoingMessage.MaxDeduplicationKeyLength).IsUnicode(false);
        builder.Property(x => x.LockToken).IsConcurrencyToken();

        builder.HasOne<MessageCampaign>()
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict);
        // A reminder's history outlives its appointment.
        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // The same reminder or campaign recipient is never queued twice, even by two instances at once.
        builder.HasIndex(x => new { x.TenantId, x.DeduplicationKey })
            .IsUnique()
            .HasFilter("[DeduplicationKey] IS NOT NULL");
        // The worker claims due messages across businesses.
        builder.HasIndex(x => new { x.NextAttemptAt, x.CreatedAt })
            .HasDatabaseName("IX_OutgoingMessages_Pending")
            .HasFilter("[Status] = 0");
        builder.HasIndex(x => new { x.TenantId, x.CampaignId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.Purpose, x.CreatedAt });
        // Delivery reports from the webhook find the message by its provider id.
        builder.HasIndex(x => x.ProviderMessageId)
            .HasFilter("[ProviderMessageId] IS NOT NULL");
    }
}

public class MessagingOptOutConfiguration : IEntityTypeConfiguration<MessagingOptOut>
{
    public void Configure(EntityTypeBuilder<MessagingOptOut> builder)
    {
        builder.ToTable("MessagingOptOuts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(MessagingPhone.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Source).HasConversion<int>().IsRequired();
        builder.Ignore(x => x.IsActive);

        builder.HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
    }
}
