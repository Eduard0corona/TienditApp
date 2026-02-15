using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class WhatsAppConversationConfiguration : IEntityTypeConfiguration<WhatsAppConversation>
{
    public void Configure(EntityTypeBuilder<WhatsAppConversation> builder)
    {
        builder.ToTable("WhatsAppConversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.CustomerPhone).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(c => c.CustomerName).HasMaxLength(200);
        builder.Property(c => c.LastMessageBody).HasMaxLength(500);
        builder.Property(c => c.LastMessageAt).IsRequired();
        builder.Property(c => c.UnreadCount).IsRequired().HasDefaultValue(0);
        builder.Property(c => c.IsArchived).IsRequired().HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.LastMessageAt })
            .IsDescending(false, true);

        builder.HasIndex(c => new { c.TenantId, c.CustomerPhone }).IsUnique();

        builder.HasMany(c => c.Messages)
            .WithOne(m => m.Conversation)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
