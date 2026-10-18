using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The favorite businesses of a customer account (Fase 24F). They belong to the account, not to a business, so they have
/// no business filter: every read goes by the account's own id. They go with the account when it is deleted; a business is
/// never deleted (it is suspended or cancelled), so its favorites stay and the account stops seeing it.
/// </summary>
public class FavoriteBusinessConfiguration : IEntityTypeConfiguration<FavoriteBusiness>
{
    public void Configure(EntityTypeBuilder<FavoriteBusiness> builder)
    {
        builder.ToTable("FavoriteBusinesses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.BusinessId).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.BusinessId)
            .OnDelete(DeleteBehavior.Restrict);

        // One favorite per business and account; it is also the account's list.
        builder.HasIndex(x => new { x.UserId, x.BusinessId }).IsUnique();
    }
}
