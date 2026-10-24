using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The CFDI invoices of the orders (Fase 28A, ADR 0045; in installments too, Fase 28C). An order has at most one live
/// invoice (stamping, stamped or waiting for its cancellation), a folio is never used twice in a business, and each request to the PAC has its own key.
/// The rest of what the domain guarantees is repeated here, so a row the domain never writes is refused.
/// </summary>
public class OrderInvoiceConfiguration : IEntityTypeConfiguration<OrderInvoice>
{
    public void Configure(EntityTypeBuilder<OrderInvoice> builder)
    {
        builder.ToTable("OrderInvoices", table =>
        {
            table.HasCheckConstraint("CK_OrderInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_OrderInvoices_Folio", "[Folio] > 0");
            table.HasCheckConstraint(
                "CK_OrderInvoices_Amounts",
                "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
            // Paid in one exhibition with the form that paid it, or in installments «por definir» (Fase 28C).
            table.HasCheckConstraint(
                "CK_OrderInvoices_PaymentMethod",
                "([PaymentMethod] = 'PUE' AND [PaymentForm] <> '99') OR ([PaymentMethod] = 'PPD' AND [PaymentForm] = '99')");
            table.HasCheckConstraint(
                "CK_OrderInvoices_Stamp",
                "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL"
                + " AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL)"
                + " OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL"
                + " AND [XmlFileKey] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_OrderInvoices_Refused",
                "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_OrderInvoices_Cancellation",
                "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL"
                + " AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 1 AND [CancelledAt] IS NULL)"
                + " OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL"
                + " AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
            table.HasCheckConstraint("CK_OrderInvoices_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 3)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(OrderInvoice.MaxSerieLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.PaymentForm).HasMaxLength(2).IsUnicode(false).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(3).IsUnicode(false).IsRequired();
        builder.Property(x => x.Document).IsRequired();
        builder.Property(x => x.SubTotal).HasPrecision(18, 2);
        builder.Property(x => x.TaxTotal).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);
        builder.Property(x => x.Uuid).HasMaxLength(OrderInvoice.UuidLength).IsUnicode(false);
        builder.Property(x => x.ProviderInvoiceId).HasMaxLength(OrderInvoice.MaxProviderInvoiceIdLength);
        builder.Property(x => x.XmlFileKey).HasMaxLength(OrderInvoice.MaxFileKeyLength);
        builder.Property(x => x.PdfFileKey).HasMaxLength(OrderInvoice.MaxFileKeyLength);
        builder.Property(x => x.FailureMessage).HasMaxLength(OrderInvoice.MaxFailureMessageLength);
        builder.Property(x => x.CancellationMotive).HasConversion<int?>();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsLive);
        builder.Ignore(x => x.Number);

        builder.OwnsOne(x => x.Receiver, receiver => CfdiReceiverColumns.Map(receiver, "Receiver"));
        builder.Navigation(x => x.Receiver).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        // One live invoice per order: stamping, stamped or waiting for its cancellation.
        builder.HasIndex(x => new { x.TenantId, x.OrderId }, "IX_OrderInvoices_TenantId_OrderId_Live")
            .IsUnique()
            .HasFilter("[Status] IN (0, 1, 3)");
        builder.HasIndex(x => new { x.TenantId, x.Serie, x.Folio }).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Uuid }).IsUnique().HasFilter("[Uuid] IS NOT NULL");
        // An order's invoices, and the ones the worker follows (stamping or waiting for their cancellation).
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => new { x.Status, x.RequestedAt }).HasFilter("[Status] IN (0, 3)");
    }
}
