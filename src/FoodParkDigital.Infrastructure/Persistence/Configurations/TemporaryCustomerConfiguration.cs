using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class TemporaryCustomerConfiguration : IEntityTypeConfiguration<TemporaryCustomer>
{
    public void Configure(EntityTypeBuilder<TemporaryCustomer> builder)
    {
        builder.ToTable("TemporaryCustomers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PhoneNormalized).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired()
            .HasDefaultValue(TemporaryCustomerStatus.PendingInvited);
        builder.Property(x => x.ClaimedUserId).IsRequired(false);

        builder.HasIndex(x => new { x.TenantId, x.PhoneNormalized }).IsUnique();
        builder.HasIndex(x => x.PhoneNormalized);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.ClaimedUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
