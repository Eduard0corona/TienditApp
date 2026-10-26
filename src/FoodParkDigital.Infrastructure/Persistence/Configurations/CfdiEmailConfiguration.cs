using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The emails of the CFDI of the orders' invoices to their customers (Fase 28E, ADR 0049): what each one carried, to whom,
/// who asked for it and how it ended. What the domain guarantees is repeated here, so a row the domain never writes is
/// refused.
/// </summary>
public class CfdiEmailConfiguration : IEntityTypeConfiguration<CfdiEmail>
{
    public void Configure(EntityTypeBuilder<CfdiEmail> builder)
    {
        builder.ToTable("CfdiEmails", table =>
        {
            table.HasCheckConstraint("CK_CfdiEmails_Document", "[Document] IN (0, 1, 2)");
            // The email of an invoice carries that invoice.
            table.HasCheckConstraint("CK_CfdiEmails_Invoice", "[Document] <> 0 OR [CfdiId] = [OrderInvoiceId]");
            table.HasCheckConstraint(
                "CK_CfdiEmails_Outcome",
                "([SentAt] IS NULL OR [FailedAt] IS NULL)"
                + " AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2, 3)))"
                + " AND ([SentWithoutPdf] = 0 OR [SentAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_CfdiEmails_Attempts",
                "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OrderInvoiceId).IsRequired();
        builder.Property(x => x.Document).HasConversion<int>();
        builder.Property(x => x.CfdiId).IsRequired();
        builder.Property(x => x.To).HasMaxLength(InvoiceEmailAddress.MaxLength).IsRequired();
        builder.Property(x => x.Failure).HasConversion<int?>();
        builder.Ignore(x => x.IsPending);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderInvoice>()
            .WithMany()
            .HasForeignKey(x => x.OrderInvoiceId)
            .OnDelete(DeleteBehavior.NoAction);

        // The emails of a CFDI (its latest ones, and how many in the last day), of an order's invoice, and of a business in
        // the last day.
        builder.HasIndex(x => new { x.TenantId, x.CfdiId, x.RequestedAt });
        builder.HasIndex(x => x.OrderInvoiceId);
        builder.HasIndex(x => new { x.TenantId, x.RequestedAt });
    }
}
