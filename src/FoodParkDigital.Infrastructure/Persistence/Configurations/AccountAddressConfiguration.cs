using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The addresses of a customer account (Fase 24B). They belong to the account, not to a business, so they have no
/// business filter: every read goes by the account's own id. They go with the account when it is deleted.
/// </summary>
public class AccountAddressConfiguration : IEntityTypeConfiguration<AccountAddress>
{
    public void Configure(EntityTypeBuilder<AccountAddress> builder)
    {
        // The postal code with the rule of the zones, also for a row written by hand.
        builder.ToTable("AccountAddresses", table =>
            table.HasCheckConstraint(
                "CK_AccountAddresses_PostalCode",
                "LEN([PostalCode]) = 5 AND [PostalCode] NOT LIKE '%[^0-9]%'"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(AccountAddress.MaxLabelLength).IsRequired();
        builder.Property(x => x.PostalCode).HasMaxLength(DeliveryZonePostalCode.Length).IsUnicode(false).IsRequired();
        builder.Property(x => x.AddressLine).HasMaxLength(DeliveryAddress.MaxAddressLineLength).IsRequired();
        builder.Property(x => x.References).HasMaxLength(DeliveryAddress.MaxReferencesLength);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The account's list, the last one used first.
        builder.HasIndex(x => new { x.UserId, x.LastUsedAt });
    }
}
