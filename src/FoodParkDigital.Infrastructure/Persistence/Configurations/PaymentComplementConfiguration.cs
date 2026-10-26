using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The payment complements of the invoices in installments (Fase 28C, ADR 0047), with the same states, stamp and
/// cancellation as the invoices: a payment has at most one live complement, a folio is never used twice in a business and
/// each request to the PAC has its own key. The rest of what the domain guarantees is repeated here, so a row the domain
/// never writes is refused.
/// </summary>
public class PaymentComplementConfiguration : IEntityTypeConfiguration<PaymentComplement>
{
    public void Configure(EntityTypeBuilder<PaymentComplement> builder)
    {
        builder.ToTable("PaymentComplements", table =>
        {
            table.HasCheckConstraint("CK_PaymentComplements_Status", "[Status] IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_PaymentComplements_Folio", "[Folio] > 0");
            table.HasCheckConstraint(
                "CK_PaymentComplements_Amounts",
                "[Amount] > 0 AND [PreviousBalance] > 0 AND [Amount] <= [PreviousBalance]"
                + " AND [RemainingBalance] = [PreviousBalance] - [Amount]");
            table.HasCheckConstraint(
                "CK_PaymentComplements_Installment", $"[Installment] BETWEEN 1 AND {PaymentComplement.MaxInstallment}");
            table.HasCheckConstraint("CK_PaymentComplements_PaymentForm", "[PaymentForm] NOT IN ('99', '30')");
            table.HasCheckConstraint(
                "CK_PaymentComplements_Stamp",
                "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL"
                + " AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL)"
                + " OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL"
                + " AND [XmlFileKey] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_PaymentComplements_Refused",
                "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_PaymentComplements_Cancellation",
                "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL"
                + " AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 1 AND [CancelledAt] IS NULL)"
                + " OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL"
                + " AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
            table.HasCheckConstraint(
                "CK_PaymentComplements_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderInvoiceId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.OrderPaymentId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(Cfdi.MaxSerieLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PaymentForm).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.PreviousBalance).HasPrecision(18, 2);
        builder.Property(x => x.RemainingBalance).HasPrecision(18, 2);
        builder.Property(x => x.Document).IsRequired();
        builder.Property(x => x.Uuid).HasMaxLength(Cfdi.UuidLength).IsUnicode(false);
        builder.Property(x => x.ProviderInvoiceId).HasMaxLength(Cfdi.MaxProviderInvoiceIdLength);
        builder.Property(x => x.XmlFileKey).HasMaxLength(Cfdi.MaxFileKeyLength);
        builder.Property(x => x.PdfFileKey).HasMaxLength(Cfdi.MaxFileKeyLength);
        builder.Property(x => x.FailureMessage).HasMaxLength(Cfdi.MaxFailureMessageLength);
        builder.Property(x => x.CancellationMotive).HasConversion<int?>();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsLive);
        builder.Ignore(x => x.Number);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderInvoice>()
            .WithMany()
            .HasForeignKey(x => x.OrderInvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderPayment>()
            .WithMany()
            .HasForeignKey(x => x.OrderPaymentId)
            .OnDelete(DeleteBehavior.NoAction);

        // One live complement per payment: stamping, stamped or waiting for its cancellation.
        builder.HasIndex(x => new { x.TenantId, x.OrderPaymentId }, "IX_PaymentComplements_TenantId_OrderPaymentId_Live")
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 3)");
        // The live complements of an invoice count its installments once each, in the order they were issued.
        builder.HasIndex(x => new { x.OrderInvoiceId, x.Installment }, "IX_PaymentComplements_OrderInvoiceId_Installment_Live")
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 3)");
        builder.HasIndex(x => new { x.TenantId, x.Serie, x.Folio }).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Uuid }).IsUnique().HasFilter("[Uuid] IS NOT NULL");
        // An order's complements, a payment's, and the ones the worker follows.
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.OrderPaymentId);
        builder.HasIndex(x => new { x.Status, x.RequestedAt }).HasFilter("[Status] IN (0, 3)");
        // A month of the business's CFDI, in Reportes › CFDI emitidos (Fase 28H).
        builder.HasIndex(x => new { x.TenantId, x.IssuedAt });
    }
}
