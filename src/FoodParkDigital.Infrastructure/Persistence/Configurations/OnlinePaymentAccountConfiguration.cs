using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The business's own Mercado Pago account (Fase 24I, ADR 0031): one per business, with its keys encrypted. Two businesses
/// of the same owner may connect the same account, so its id in Mercado Pago is looked up, not unique.
/// </summary>
public class OnlinePaymentAccountConfiguration : IEntityTypeConfiguration<OnlinePaymentAccount>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentAccount> builder)
    {
        builder.ToTable("OnlinePaymentAccounts", table =>
            table.HasCheckConstraint("CK_OnlinePaymentAccounts_ProviderUserId", "[ProviderUserId] > 0"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProtectedAccessToken).HasMaxLength(OnlinePaymentAccount.MaxProtectedTokenLength).IsRequired();
        builder.Property(x => x.ProtectedRefreshToken).HasMaxLength(OnlinePaymentAccount.MaxProtectedTokenLength).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TenantId).IsUnique();
        // A payment notification names the account it was collected by.
        builder.HasIndex(x => x.ProviderUserId);
    }
}
