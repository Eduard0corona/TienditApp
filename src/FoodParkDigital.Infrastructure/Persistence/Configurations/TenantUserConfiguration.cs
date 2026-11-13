using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class TenantUserConfiguration : IEntityTypeConfiguration<TenantUser>
{
    public void Configure(EntityTypeBuilder<TenantUser> builder)
    {
        builder.ToTable("TenantUsers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Role).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.PermissionCodes).HasMaxLength(1000);
        // Fase 29N (ADR 0068): whether the member gets an email with each request online, as they chose; null until they
        // choose (the owners get them), which is also what the version before it saves.
        builder.Property(x => x.RequestEmails);
        builder.Ignore(x => x.GetsRequestEmails);
        // Fase 29O (ADR 0069): whether the member gets an email with each answer to a quote from its link, as they chose; null
        // until they choose (the owners get them), which is also what the version before it saves.
        builder.Property(x => x.QuoteReplyEmails);
        builder.Ignore(x => x.GetsQuoteReplyEmails);
        // Fase 30B (ADR 0082): null until they choose, when only the owners get them.
        builder.Property(x => x.OrderCancellationEmails);
        builder.Ignore(x => x.GetsOrderCancellationEmails);
        // Fase 31K (ADR 0091): the new reviews; null until they choose, when only the owners get them.
        builder.Property(x => x.ReviewEmails);
        builder.Ignore(x => x.GetsReviewEmails);

        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.UserId);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
