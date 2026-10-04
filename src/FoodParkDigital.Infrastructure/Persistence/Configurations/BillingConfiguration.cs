using FoodParkDigital.Domain.Billing.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(Plan.MaxCodeLength).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(Plan.MaxNameLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Plan.MaxDescriptionLength);
        builder.Property(x => x.MonthlyPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Modules).HasConversion<int>().IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        // Business rule: one plan for the businesses that sign up on their own, enforced by the database.
        builder.HasIndex(x => x.IsDefaultForNewBusinesses)
            .IsUnique()
            .HasFilter("[IsDefaultForNewBusinesses] = 1")
            .HasDatabaseName("IX_Plans_DefaultForNewBusinesses");
    }
}

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("TenantSubscriptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.StatusReason).HasMaxLength(TenantSubscription.MaxReasonLength);
        builder.Ignore(x => x.CoveredUntil);
        builder.Ignore(x => x.SuspendsAt);
        builder.Ignore(x => x.HasAccess);

        // One subscription per business.
        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasIndex(x => x.Status);

        builder.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Payments)
            .WithOne()
            .HasForeignKey(x => x.SubscriptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class SubscriptionPaymentConfiguration : IEntityTypeConfiguration<SubscriptionPayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionPayment> builder)
    {
        builder.ToTable("SubscriptionPayments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Method).HasConversion<int>().IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(SubscriptionPayment.MaxReferenceLength);
        builder.Property(x => x.RecordedBy).HasMaxLength(256).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.PaidAt });
        builder.HasIndex(x => x.PaidAt);
    }
}
