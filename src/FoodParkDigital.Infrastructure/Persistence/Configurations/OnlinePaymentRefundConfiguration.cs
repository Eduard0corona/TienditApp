using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// Money paid online that goes back through Mercado Pago (Fase 24I, ADR 0031). An approved payment is given back
/// automatically once at most (the notification, the customer's return and the sweep may all see it), and each refund of
/// the business asks Mercado Pago once. What went back in Mercado Pago without TienditApp asking (Fase 24K, ADR 0044) is
/// recorded once: each refund of Mercado Pago by its id, and the lost chargeback once per payment.
/// </summary>
public class OnlinePaymentRefundConfiguration : IEntityTypeConfiguration<OnlinePaymentRefund>
{
    public void Configure(EntityTypeBuilder<OnlinePaymentRefund> builder)
    {
        builder.ToTable("OnlinePaymentRefunds", table =>
        {
            table.HasCheckConstraint("CK_OnlinePaymentRefunds_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_OnlinePaymentRefunds_Status", "[Status] IN (0, 1, 2, 3)");
            table.HasCheckConstraint(
                "CK_OnlinePaymentRefunds_Link",
                "([Reason] IN (1, 2) AND [CheckoutId] IS NOT NULL AND [OrderId] IS NULL AND [OrderRefundId] IS NULL)"
                + " OR ([Reason] IN (3, 4, 5, 6) AND [CheckoutId] IS NULL AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_OnlinePaymentRefunds_Settled",
                "([Status] = 0 AND [SettledAt] IS NULL AND [FailureMessage] IS NULL)"
                + " OR ([Status] = 1 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NULL)"
                + " OR ([Status] = 2 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL)"
                + " OR ([Status] = 3 AND [SettledAt] IS NOT NULL AND [FailureMessage] IS NOT NULL AND [ProviderRefundId] IS NOT NULL)");
            // Made in Mercado Pago: already done, and a refund of Mercado Pago by its id (a lost chargeback has none).
            table.HasCheckConstraint(
                "CK_OnlinePaymentRefunds_MadeInMercadoPago",
                "[Reason] NOT IN (4, 5, 6) OR ([Status] = 1 AND ([Reason] = 6 OR [ProviderRefundId] IS NOT NULL))");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PaymentId).HasMaxLength(OnlinePaymentRefund.MaxPaymentIdLength).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(OnlinePaymentRefund.MaxProviderRefundIdLength);
        builder.Property(x => x.FailureMessage).HasMaxLength(OnlinePaymentRefund.MaxFailureMessageLength);
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OnlineCheckout>()
            .WithMany()
            .HasForeignKey(x => x.CheckoutId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderRefund>()
            .WithMany()
            .HasForeignKey(x => x.OrderRefundId)
            .OnDelete(DeleteBehavior.NoAction);

        // One automatic refund per approved payment.
        builder.HasIndex(x => new { x.TenantId, x.PaymentId }).IsUnique().HasFilter("[OrderRefundId] IS NULL");
        // Each refund of Mercado Pago is recorded once (Fase 24K), and a lost chargeback once per payment.
        builder.HasIndex(x => new { x.TenantId, x.ProviderRefundId }).IsUnique().HasFilter("[ProviderRefundId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.PaymentId }, "IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback")
            .IsUnique()
            .HasFilter("[Reason] = 6");
        builder.HasIndex(x => x.OrderRefundId).IsUnique().HasFilter("[OrderRefundId] IS NOT NULL");
        // An order's refunds, and its checkout's.
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.CheckoutId);
    }
}
