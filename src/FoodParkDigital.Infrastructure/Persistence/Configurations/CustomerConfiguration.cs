using FoodParkDigital.Domain.Customers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.IsPublicGeneral).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);

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
