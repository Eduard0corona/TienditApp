using FoodParkDigital.Domain.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ProductPlatformPriceConfiguration : IEntityTypeConfiguration<ProductPlatformPrice>
{
    public void Configure(EntityTypeBuilder<ProductPlatformPrice> builder)
    {
        // Only the delivery platforms (Rappi 3, Uber Eats 4, DiDi Food 5), and a price as a menu would have it: the upper
        // limit of policy lives in ProductPlatformPrice and can change without a migration.
        builder.ToTable("ProductPlatformPrices", table =>
        {
            table.HasCheckConstraint("CK_ProductPlatformPrices_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint("CK_ProductPlatformPrices_Price", "[Price] > 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)").IsRequired();

        // One price per product and platform; it is also the index of the product's key (ON DELETE CASCADE).
        builder.HasIndex(x => new { x.ProductId, x.Channel }).IsUnique();
        // Vender and the price list read every price of one platform of the business.
        builder.HasIndex(x => new { x.TenantId, x.Channel });
    }
}

public class ProductOptionPlatformPriceConfiguration : IEntityTypeConfiguration<ProductOptionPlatformPrice>
{
    public void Configure(EntityTypeBuilder<ProductOptionPlatformPrice> builder)
    {
        // The same limits as what an option adds in the menu (ProductOption.MaxPriceDelta), either way.
        builder.ToTable("ProductOptionPlatformPrices", table =>
        {
            table.HasCheckConstraint("CK_ProductOptionPlatformPrices_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint("CK_ProductOptionPlatformPrices_PriceDelta", "[PriceDelta] BETWEEN -100000 AND 100000");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.PriceDelta).HasColumnType("decimal(18,2)").IsRequired();

        // One per option and platform; it is also the index of the option's key (ON DELETE CASCADE).
        builder.HasIndex(x => new { x.OptionId, x.Channel }).IsUnique();
        // Vender and the price list read every price of one platform of the business.
        builder.HasIndex(x => new { x.TenantId, x.Channel });
    }
}
