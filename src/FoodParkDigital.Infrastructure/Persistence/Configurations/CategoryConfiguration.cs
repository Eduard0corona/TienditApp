using FoodParkDigital.Domain.Catalog.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.TenantId).IsRequired();

        builder.HasIndex(c => c.TenantId);
        // One name per category in a business, archived ones included: the use cases compare with Category.NameKey
        // (capitals and spaces aside); this backs them when two saves meet, with the collation of the database
        // (SQL_Latin1_General_CP1_CI_AS: without capitals, spaces at the end or the width of a letter, and «ß» is «ss»).
        // CategoryNames asks the database too, so what only the collation takes as the same is answered, not failed.
        builder.HasIndex(c => new { c.TenantId, c.Name }).IsUnique();

        builder.HasMany(c => c.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
