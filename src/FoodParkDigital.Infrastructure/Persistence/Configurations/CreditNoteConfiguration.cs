using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The credit notes (CFDI de egreso) of the refunds and write-offs of the orders with an invoice (Fase 28D, ADR 0048), and of
/// the refunds of the sales a global invoice covers (Fase 28I, ADR 0054), with
/// the same states, stamp and cancellation as the invoices: an adjustment has at most one live credit note, a folio is never
/// used twice in a business and each request to the PAC has its own key. The rest of what the domain guarantees is repeated
/// here, so a row the domain never writes is refused.
/// </summary>
public class CreditNoteConfiguration : IEntityTypeConfiguration<CreditNote>
{
    public void Configure(EntityTypeBuilder<CreditNote> builder)
    {
        builder.ToTable("CreditNotes", table =>
        {
            table.HasCheckConstraint("CK_CreditNotes_Status", "[Status] IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_CreditNotes_Folio", "[Folio] > 0");
            table.HasCheckConstraint(
                "CK_CreditNotes_Amounts",
                "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
            table.HasCheckConstraint(
                "CK_CreditNotes_AfterInstallment", $"[AfterInstallment] BETWEEN 0 AND {PaymentComplement.MaxInstallment}");
            // It credits a refund, settled with the refund's form, or a write-off, settled by «Condonación» (15).
            table.HasCheckConstraint(
                "CK_CreditNotes_Adjustment",
                "([OrderRefundId] IS NOT NULL AND [OrderWriteOffId] IS NULL AND [PaymentForm] NOT IN ('99', '30', '15'))"
                + " OR ([OrderRefundId] IS NULL AND [OrderWriteOffId] IS NOT NULL AND [PaymentForm] = '15')");
            table.HasCheckConstraint(
                "CK_CreditNotes_Stamp",
                "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL"
                + " AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL)"
                + " OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL"
                + " AND [XmlFileKey] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_CreditNotes_Refused",
                "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_CreditNotes_Cancellation",
                "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL"
                + " AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 1 AND [CancelledAt] IS NULL)"
                + " OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL"
                + " AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
            table.HasCheckConstraint(
                "CK_CreditNotes_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
            // It credits an order's invoice or a global invoice (Fase 28I), never both: a global invoice's, a refund or a
            // write-off of one of its sales (Fase 28J), paid in one exhibition.
            table.HasCheckConstraint(
                "CK_CreditNotes_Invoice",
                "([OrderInvoiceId] IS NOT NULL AND [GlobalInvoiceId] IS NULL)"
                + " OR ([OrderInvoiceId] IS NULL AND [GlobalInvoiceId] IS NOT NULL AND [AfterInstallment] = 0)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(Cfdi.MaxSerieLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PaymentForm).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(x => x.SubTotal).HasPrecision(18, 2);
        builder.Property(x => x.TaxTotal).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);
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
        builder.Ignore(x => x.IsForRefund);
        builder.Ignore(x => x.IsForGlobalInvoice);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderInvoice>()
            .WithMany()
            .HasForeignKey(x => x.OrderInvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<GlobalInvoice>()
            .WithMany()
            .HasForeignKey(x => x.GlobalInvoiceId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderRefund>()
            .WithMany()
            .HasForeignKey(x => x.OrderRefundId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<OrderWriteOff>()
            .WithMany()
            .HasForeignKey(x => x.OrderWriteOffId)
            .OnDelete(DeleteBehavior.NoAction);

        // One live credit note per refund and per write-off: stamping, stamped or waiting for its cancellation.
        builder.HasIndex(x => x.OrderRefundId, "IX_CreditNotes_OrderRefundId_Live")
            .IsUnique()
            .HasFilter("[OrderRefundId] IS NOT NULL AND [Status] IN (0, 1, 3)");
        builder.HasIndex(x => x.OrderWriteOffId, "IX_CreditNotes_OrderWriteOffId_Live")
            .IsUnique()
            .HasFilter("[OrderWriteOffId] IS NOT NULL AND [Status] IN (0, 1, 3)");
        builder.HasIndex(x => new { x.TenantId, x.Serie, x.Folio }).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Uuid }).IsUnique().HasFilter("[Uuid] IS NOT NULL");
        // An order's credit notes, an invoice's, a global invoice's, and the ones the worker follows.
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.OrderInvoiceId);
        builder.HasIndex(x => x.GlobalInvoiceId).HasFilter("[GlobalInvoiceId] IS NOT NULL");
        builder.HasIndex(x => new { x.Status, x.RequestedAt }).HasFilter("[Status] IN (0, 3)");
        // A month of the business's CFDI, in Reportes › CFDI emitidos (Fase 28H).
        builder.HasIndex(x => new { x.TenantId, x.IssuedAt });
    }
}
