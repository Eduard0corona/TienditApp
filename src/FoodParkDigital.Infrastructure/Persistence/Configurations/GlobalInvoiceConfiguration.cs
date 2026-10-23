using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The global invoices of each business (Fase 28B, ADR 0046), with the same states, stamp and cancellation as the invoices
/// of the orders: a folio is never used twice in a business and each request to the PAC has its own key. The rest of what
/// the domain guarantees is repeated here, so a row the domain never writes is refused.
/// </summary>
public class GlobalInvoiceConfiguration : IEntityTypeConfiguration<GlobalInvoice>
{
    public void Configure(EntityTypeBuilder<GlobalInvoice> builder)
    {
        builder.ToTable("GlobalInvoices", table =>
        {
            table.HasCheckConstraint("CK_GlobalInvoices_Status", "[Status] IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_GlobalInvoices_Folio", "[Folio] > 0");
            table.HasCheckConstraint(
                "CK_GlobalInvoices_Amounts",
                "[Total] > 0 AND [SubTotal] >= 0 AND [TaxTotal] >= 0 AND [SubTotal] + [TaxTotal] = [Total]");
            table.HasCheckConstraint("CK_GlobalInvoices_PaymentMethod", "[PaymentMethod] = 'PUE'");
            table.HasCheckConstraint(
                "CK_GlobalInvoices_Period",
                "[Periodicity] IN (1, 2, 3, 4, 5) AND [PeriodFrom] <= [PeriodTo] AND [Year] = YEAR([PeriodFrom])"
                + " AND [Months] IN ('01', '02', '03', '04', '05', '06', '07', '08', '09', '10', '11', '12',"
                + " '13', '14', '15', '16', '17', '18') AND [OrderCount] > 0");
            table.HasCheckConstraint(
                "CK_GlobalInvoices_Stamp",
                "([Status] IN (0, 2) AND [Uuid] IS NULL AND [ProviderInvoiceId] IS NULL AND [StampedAt] IS NULL"
                + " AND [XmlFileKey] IS NULL AND [PdfFileKey] IS NULL)"
                + " OR ([Status] IN (1, 3, 4) AND [Uuid] IS NOT NULL AND [ProviderInvoiceId] IS NOT NULL AND [StampedAt] IS NOT NULL"
                + " AND [XmlFileKey] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_GlobalInvoices_Refused",
                "([Status] = 2 AND [RefusedAt] IS NOT NULL AND [FailureMessage] IS NOT NULL) OR ([Status] <> 2 AND [RefusedAt] IS NULL)");
            table.HasCheckConstraint(
                "CK_GlobalInvoices_Cancellation",
                "([Status] IN (0, 2) AND [CancellationMotive] IS NULL AND [CancellationRequestedAt] IS NULL"
                + " AND [CancellationRequestedByUserId] IS NULL AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 1 AND [CancelledAt] IS NULL)"
                + " OR ([Status] IN (3, 4) AND [CancellationMotive] IS NOT NULL AND [CancellationRequestedAt] IS NOT NULL"
                + " AND [CancellationRequestedByUserId] IS NOT NULL AND (([Status] = 3 AND [CancelledAt] IS NULL)"
                + " OR ([Status] = 4 AND [CancelledAt] IS NOT NULL)))");
            table.HasCheckConstraint("CK_GlobalInvoices_CancellationMotive", "[CancellationMotive] IS NULL OR [CancellationMotive] IN (2, 4)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(CfdiInvoice.MaxSerieLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Periodicity).HasConversion<int>();
        builder.Property(x => x.Months).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(x => x.PaymentForm).HasMaxLength(2).IsUnicode(false).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(3).IsUnicode(false).IsRequired();
        builder.Property(x => x.Document).IsRequired();
        builder.Property(x => x.SubTotal).HasPrecision(18, 2);
        builder.Property(x => x.TaxTotal).HasPrecision(18, 2);
        builder.Property(x => x.Total).HasPrecision(18, 2);
        builder.Property(x => x.Uuid).HasMaxLength(CfdiInvoice.UuidLength).IsUnicode(false);
        builder.Property(x => x.ProviderInvoiceId).HasMaxLength(CfdiInvoice.MaxProviderInvoiceIdLength);
        builder.Property(x => x.XmlFileKey).HasMaxLength(CfdiInvoice.MaxFileKeyLength);
        builder.Property(x => x.PdfFileKey).HasMaxLength(CfdiInvoice.MaxFileKeyLength);
        builder.Property(x => x.FailureMessage).HasMaxLength(CfdiInvoice.MaxFailureMessageLength);
        builder.Property(x => x.CancellationMotive).HasConversion<int?>();
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.IsLive);
        builder.Ignore(x => x.Number);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Orders)
            .WithOne()
            .HasForeignKey(x => x.GlobalInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Orders).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Serie, x.Folio }).IsUnique();
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Uuid }).IsUnique().HasFilter("[Uuid] IS NOT NULL");
        // The list of a business's global invoices (newest period first), and the ones the worker follows.
        builder.HasIndex(x => new { x.TenantId, x.PeriodFrom });
        builder.HasIndex(x => new { x.Status, x.RequestedAt }).HasFilter("[Status] IN (0, 3)");
    }
}

/// <summary>
/// The sales each global invoice covers (Fase 28B). A sale is in at most one global invoice that stands with the SAT: the
/// rows of a refused or cancelled one are kept, released.
/// </summary>
public class GlobalInvoiceOrderConfiguration : IEntityTypeConfiguration<GlobalInvoiceOrder>
{
    public void Configure(EntityTypeBuilder<GlobalInvoiceOrder> builder)
    {
        builder.ToTable("GlobalInvoiceOrders", table => table.HasCheckConstraint("CK_GlobalInvoiceOrders_Total", "[Total] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderNumber).HasMaxLength(GlobalInvoiceOrder.MaxOrderNumberLength).IsRequired();
        builder.Property(x => x.Total).HasPrecision(18, 2);

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        // A sale in one live global invoice at most.
        builder.HasIndex(x => new { x.TenantId, x.OrderId }, "IX_GlobalInvoiceOrders_TenantId_OrderId_Live")
            .IsUnique()
            .HasFilter("[Released] = 0");
        builder.HasIndex(x => x.OrderId);
    }
}
