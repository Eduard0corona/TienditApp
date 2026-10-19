using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>An attempt to connect the business's Mercado Pago account: its id is the OAuth state (Fase 24I).</summary>
public class OnlinePaymentAuthorizationConfiguration : IEntityTypeConfiguration<OnlinePaymentAuthorization>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentAuthorization> builder)
    {
        builder.ToTable("OnlinePaymentAuthorizations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.CodeVerifier).HasMaxLength(OnlinePaymentAuthorization.MaxCodeVerifierLength).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Old attempts are deleted by when they expired.
        builder.HasIndex(x => x.ExpiresAt);
    }
}
