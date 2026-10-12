using FoodParkDigital.Domain.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class ProductOptionGroupConfiguration : IEntityTypeConfiguration<ProductOptionGroup>
{
    public void Configure(EntityTypeBuilder<ProductOptionGroup> builder)
    {
        builder.ToTable("ProductOptionGroups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(ProductOptionGroup.MaxNameLength).IsRequired();
        builder.Ignore(x => x.IsRequired);

        builder.HasMany(x => x.Options)
            .WithOne()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.ProductId });
    }
}

public class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> builder)
    {
        builder.ToTable("ProductOptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(ProductOption.MaxNameLength).IsRequired();
        builder.Property(x => x.PriceDelta).HasColumnType("decimal(18,2)").IsRequired();

        // What it adds in the app of each delivery platform, removed with the option.
        builder.HasMany(x => x.PlatformPrices)
            .WithOne()
            .HasForeignKey(price => price.OptionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.PlatformPrices).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.GroupId });
    }
}
