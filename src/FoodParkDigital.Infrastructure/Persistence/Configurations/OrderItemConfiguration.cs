using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Quantity).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(i => i.ReturnedQuantity).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(i => i.TotalPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(300);
        builder.Property(i => i.TaxRate).HasColumnType("decimal(5,2)");
        builder.Ignore(i => i.VatRate);

        // Unit and CFDI codes copied from the product when it was sold. The database defaults only fill the
        // lines sold before these columns existed; EF always writes the values of new lines.
        builder.Property(i => i.Unit)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(UnitType.Unit)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(i => i.SatProductCode)
            .HasMaxLength(8)
            .HasDefaultValue(SatCatalog.GenericProductCode)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(i => i.SatUnitCode).HasMaxLength(3).HasDefaultValue("H87").ValueGeneratedNever().IsRequired();
        builder.Property(i => i.TaxObject)
            .HasConversion<int>()
            .HasDefaultValue(CfdiTaxObject.Subject)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Ignore(i => i.ReturnableQuantity);

        builder.HasIndex(i => i.OrderId);
    }
}
