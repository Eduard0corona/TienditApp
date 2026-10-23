using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", table =>
        {
            table.HasCheckConstraint(
                "CK_Customers_CreditTermDays",
                $"[CreditTermDays] IS NULL OR ([CreditTermDays] >= {TenantSettings.MinCreditTermDays} AND [CreditTermDays] <= {TenantSettings.MaxCreditTermDays})");
            // The fiscal data goes whole or not at all (Fase 28A), and never on the general public.
            table.HasCheckConstraint(
                "CK_Customers_FiscalData",
                "([FiscalData_Rfc] IS NULL AND [FiscalData_Name] IS NULL AND [FiscalData_PostalCode] IS NULL"
                + " AND [FiscalData_FiscalRegime] IS NULL AND [FiscalData_CfdiUse] IS NULL)"
                + " OR ([IsPublicGeneral] = 0 AND [FiscalData_Rfc] IS NOT NULL AND [FiscalData_Name] IS NOT NULL"
                + " AND [FiscalData_PostalCode] IS NOT NULL AND [FiscalData_FiscalRegime] IS NOT NULL AND [FiscalData_CfdiUse] IS NOT NULL)");
        });
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.IsPublicGeneral).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);
        builder.Property(c => c.CreditLimit).HasColumnType("decimal(18,2)");
        builder.OwnsOne(c => c.FiscalData, fiscal => CfdiReceiverColumns.Map(fiscal, "FiscalData"));

        builder.HasMany(c => c.Addresses)
            .WithOne()
            .HasForeignKey(a => a.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.Name });
        builder.HasIndex(c => new { c.TenantId, c.Phone });
        builder.HasIndex(c => new { c.TenantId, c.IsPublicGeneral }).IsUnique()
            .HasFilter("[IsPublicGeneral] = 1");
    }
}

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("CustomerAddresses");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.TenantId).IsRequired();
        builder.Property(a => a.Label).HasMaxLength(CustomerAddress.LabelMaxLength).IsRequired();
        builder.Property(a => a.AddressLine).HasMaxLength(CustomerAddress.AddressMaxLength).IsRequired();
        builder.Property(a => a.References).HasMaxLength(CustomerAddress.ReferencesMaxLength);

        builder.HasIndex(a => new { a.TenantId, a.CustomerId });
    }
}

/// <summary>The columns of a <see cref="CfdiReceiver"/> (Fase 28A): the customer's fiscal data, and an invoice's receiver.</summary>
internal static class CfdiReceiverColumns
{
    public static void Map<TOwner>(OwnedNavigationBuilder<TOwner, CfdiReceiver> receiver, string prefix) where TOwner : class
    {
        receiver.Property(r => r.Rfc).HasMaxLength(13).HasColumnName($"{prefix}_Rfc").IsRequired();
        receiver.Property(r => r.Name).HasMaxLength(CfdiReceiver.MaxNameLength).HasColumnName($"{prefix}_Name").IsRequired();
        receiver.Property(r => r.PostalCode).HasMaxLength(5).IsUnicode(false).HasColumnName($"{prefix}_PostalCode").IsRequired();
        receiver.Property(r => r.FiscalRegime).HasMaxLength(3).IsUnicode(false).HasColumnName($"{prefix}_FiscalRegime").IsRequired();
        receiver.Property(r => r.CfdiUse).HasMaxLength(4).IsUnicode(false).HasColumnName($"{prefix}_CfdiUse").IsRequired();
    }
}
