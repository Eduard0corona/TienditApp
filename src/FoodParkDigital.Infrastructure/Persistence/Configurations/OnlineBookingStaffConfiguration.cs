using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The people a business books online (Fase 24J, ADR 0032), one per team member's account, with their services and their
/// own hours. They go with the account when it is deleted; a business is never deleted (it is suspended or cancelled).
/// </summary>
public class OnlineBookingStaffConfiguration : IEntityTypeConfiguration<OnlineBookingStaff>
{
    public void Configure(EntityTypeBuilder<OnlineBookingStaff> builder)
    {
        builder.ToTable("OnlineBookingStaff");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(OnlineBookingStaff.MaxStoredNameLength).IsRequired();
        builder.Property(x => x.WeeklyHours)
            .HasConversion(OpeningHoursStorage.Converter)
            .HasMaxLength(OpeningHoursStorage.MaxLength);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Services)
            .WithOne()
            .HasForeignKey(service => service.StaffId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One per team member and business; it is also the business's list.
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
    }
}

/// <summary>A service a person who takes online appointments does; deleting the service deletes it.</summary>
public class OnlineBookingStaffServiceConfiguration : IEntityTypeConfiguration<OnlineBookingStaffService>
{
    public void Configure(EntityTypeBuilder<OnlineBookingStaffService> builder)
    {
        builder.ToTable("OnlineBookingStaffServices");
        builder.HasKey(x => new { x.StaffId, x.ProductId });

        builder.Property(x => x.TenantId).IsRequired();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting a service finds its rows.
        builder.HasIndex(x => x.ProductId);
    }
}
