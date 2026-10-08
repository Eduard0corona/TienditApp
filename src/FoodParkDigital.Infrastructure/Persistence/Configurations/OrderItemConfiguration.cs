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

        builder.Property(i => i.TenantId).IsRequired();
        builder.HasIndex(i => new { i.TenantId, i.OrderId });

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Quantity).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(i => i.ReturnedQuantity).HasColumnType("decimal(18,3)").IsRequired();
        // Units of a tab's dish cancelled after going to the kitchen and what they took off the line. The database
        // defaults only fill the lines saved before cancellations existed; EF always writes the values.
        builder.Property(i => i.CancelledQuantity)
            .HasColumnType("decimal(18,3)")
            .HasDefaultValue(0m)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(i => i.CancelledAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Ignore(i => i.ActiveQuantity);
        builder.Ignore(i => i.ChargedPrice);
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

        builder.Property(i => i.Round).HasDefaultValue(1).ValueGeneratedNever().IsRequired();
        builder.HasMany(i => i.Options)
            .WithOne()
            .HasForeignKey(o => o.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.OrderId);
    }
}

public class OrderItemCancellationConfiguration : IEntityTypeConfiguration<OrderItemCancellation>
{
    public void Configure(EntityTypeBuilder<OrderItemCancellation> builder)
    {
        builder.ToTable("OrderItemCancellations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(OrderItemCancellation.MaxReasonLength).IsRequired();
        builder.Property(x => x.CancelledAt).IsRequired();
        builder.Property(x => x.CancelledBy).HasMaxLength(OrderItemCancellation.MaxCancelledByLength).IsRequired();

        // The cancelled line must exist; deleting it is not possible while a cancellation points to it.
        builder.HasOne<OrderItem>()
            .WithMany()
            .HasForeignKey(x => x.OrderItemId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.CancelledAt });
    }
}
