using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The platform admin's accesses to a business (Fase 32G, ADR 0099). The database keeps what the domain says: a reason of
/// 10 to 500 characters, an end after the start, and a close before its time. They are history: nothing deletes them, the
/// business or the account they name included.
/// </summary>
public class SupportAccessConfiguration : IEntityTypeConfiguration<SupportAccess>
{
    public void Configure(EntityTypeBuilder<SupportAccess> builder)
    {
        builder.ToTable("SupportAccesses", table =>
        {
            table.HasCheckConstraint(
                "CK_SupportAccesses_Reason",
                $"LEN([Reason]) BETWEEN {SupportAccess.MinReasonLength} AND {SupportAccess.MaxReasonLength}");
            // How long it lasts (15 minutes to 4 hours) is the domain's: a date function in a check would not be portable to
            // the relational tests' SQLite. The database keeps that it ends after it starts.
            table.HasCheckConstraint("CK_SupportAccesses_Duration", "[ExpiresAt] > [StartedAt]");
            table.HasCheckConstraint(
                "CK_SupportAccesses_EndedAt",
                "[EndedAt] IS NULL OR ([EndedAt] >= [StartedAt] AND [EndedAt] < [ExpiresAt])");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AdminUserId).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(SupportAccess.MaxReasonLength).IsRequired();
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.AdminUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // What each request with the header reads: the admin's open access to that business.
        builder.HasIndex(x => new { x.TenantId, x.AdminUserId, x.ExpiresAt });
        // The accesses of an admin, newest first.
        builder.HasIndex(x => new { x.AdminUserId, x.StartedAt }).IsDescending(false, true);
    }
}
