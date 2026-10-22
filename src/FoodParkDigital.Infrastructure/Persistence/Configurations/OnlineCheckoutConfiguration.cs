using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// An order of the online menu the customer pays online (Fase 24I, ADR 0031). The database keeps what each status means:
/// only one awaiting payment keeps the request, a placed one has its payment and its order, and one not placed has its
/// payment and why. One order comes from one checkout at most.
/// </summary>
public class OnlineCheckoutConfiguration : IEntityTypeConfiguration<OnlineCheckout>
{
    public void Configure(EntityTypeBuilder<OnlineCheckout> builder)
    {
        builder.ToTable("OnlineCheckouts", table =>
        {
            table.HasCheckConstraint("CK_OnlineCheckouts_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_OnlineCheckouts_Status", "[Status] IN (0, 1, 2, 3)");
            table.HasCheckConstraint(
                "CK_OnlineCheckouts_Outcome",
                "([Status] = 0 AND [Request] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL)"
                + " OR ([Status] = 1 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NOT NULL AND [Failure] IS NULL)"
                + " OR ([Status] = 2 AND [Request] IS NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL AND [Failure] IS NULL)"
                + " OR ([Status] = 3 AND [Request] IS NULL AND [PaymentId] IS NOT NULL AND [PaidAt] IS NOT NULL AND [OrderId] IS NULL"
                + " AND [Failure] IS NOT NULL AND [Failure] BETWEEN 1 AND 4)");
            // Only the payment of an order has a chargeback or a claim (Fase 24K), with when it last changed.
            table.HasCheckConstraint(
                "CK_OnlineCheckouts_Dispute",
                "([Dispute] = 0 AND [DisputeUpdatedAt] IS NULL)"
                + " OR ([Dispute] IN (1, 2, 3) AND [Status] = 1 AND [DisputeUpdatedAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Request);
        builder.Property(x => x.CustomerPhone).HasMaxLength(OnlineCheckout.MaxCustomerPhoneLength);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PreferenceId).HasMaxLength(OnlineCheckout.MaxPreferenceIdLength);
        builder.Property(x => x.CheckoutUrl).HasMaxLength(OnlineCheckout.MaxCheckoutUrlLength);
        builder.Property(x => x.PaymentId).HasMaxLength(OnlineCheckout.MaxPaymentIdLength);
        builder.Property(x => x.Failure).HasConversion<int?>();
        builder.Property(x => x.FailureMessage).HasMaxLength(OnlineCheckout.MaxFailureMessageLength);
        // Every checkout has none until Mercado Pago reports one; the default keeps the rows of the previous version valid.
        builder.Property(x => x.Dispute)
            .HasConversion<int>()
            .HasDefaultValue(OnlinePaymentDispute.None)
            .ValueGeneratedNever();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsAwaitingPayment);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        // A deleted customer account leaves the checkout without it, as its orders.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CustomerUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        // The sweep looks for the ones still awaiting a payment, by when they expire.
        builder.HasIndex(x => new { x.Status, x.ExpiresAt }).HasFilter("[Status] = 0");
        // How many a phone has in course in the business.
        builder.HasIndex(x => new { x.TenantId, x.CustomerPhone }).HasFilter("[Status] = 0");
        builder.HasIndex(x => x.OrderId).IsUnique().HasFilter("[OrderId] IS NOT NULL");
        // The disputes still open, for Configuración › Pagos en línea.
        builder.HasIndex(x => new { x.TenantId, x.Dispute }).HasFilter("[Dispute] = 1");
    }
}
