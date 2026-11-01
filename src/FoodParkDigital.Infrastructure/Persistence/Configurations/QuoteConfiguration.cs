using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The versions of the quotes (Fase 29C, ADR 0053): one row per version, sharing the folio. What the aggregate guarantees is
/// repeated here, so a row it never writes is refused: a stored status is never «Vencida», a draft has no link nor validity
/// and a sent version has both, and the visit it takes off comes with its sale.
/// </summary>
public class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> builder)
    {
        builder.ToTable("Quotes", table =>
        {
            table.HasCheckConstraint("CK_Quotes_Status", "[Status] IN (0, 1, 2, 3, 5, 6)");
            table.HasCheckConstraint("CK_Quotes_Revision", "[Revision] >= 1");
            table.HasCheckConstraint(
                "CK_Quotes_ValidityDays",
                $"[ValidityDays] BETWEEN {QuoteRules.MinValidityDays} AND {QuoteRules.MaxValidityDays}");
            // Sent once (every status but a draft or a discarded draft): its validity, its link and when.
            table.HasCheckConstraint(
                "CK_Quotes_Sent",
                "([Status] IN (0, 6) AND [ValidUntil] IS NULL AND [TrackingToken] IS NULL AND [SentAt] IS NULL)"
                + " OR ([Status] IN (1, 2, 3, 5) AND [ValidUntil] IS NOT NULL AND [TrackingToken] IS NOT NULL AND [SentAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Quotes_TrackingToken",
                $"[TrackingToken] IS NULL OR LEN([TrackingToken]) = {Order.TrackingTokenLength}");
            // The customer's answer on an accepted or rejected one, which a rejected one keeps once replaced.
            table.HasCheckConstraint(
                "CK_Quotes_Reply",
                "([ReplyChannel] IS NULL AND [CustomerResponse] IS NULL AND [Status] NOT IN (2, 3))"
                + " OR ([ReplyChannel] IN (0, 1, 2, 3) AND [Status] IN (2, 3, 5))");
            table.HasCheckConstraint(
                "CK_Quotes_ClosedAt",
                "([Status] IN (0, 1) AND [ClosedAt] IS NULL) OR ([Status] IN (2, 3, 5, 6) AND [ClosedAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_Quotes_VisitCredit",
                "([VisitCreditSaleOrderId] IS NULL AND [VisitCreditAmount] IS NULL)"
                + " OR ([VisitCreditSaleOrderId] IS NOT NULL AND [VisitCreditAmount] > 0 AND [AppointmentId] IS NOT NULL)");
            table.HasCheckConstraint("CK_Quotes_Totals", "[SubTotal] >= 0 AND [Tax] >= 0 AND [Total] = [SubTotal] + [Tax]");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Number).HasMaxLength(40).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(QuoteRules.MaxCustomerNameLength).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(QuoteRules.MaxCustomerPhoneLength).IsRequired();
        builder.Property(x => x.CustomerEmail).HasMaxLength(InvoiceEmailAddress.MaxLength);
        builder.Property(x => x.Note).HasMaxLength(QuoteRules.MaxNoteLength);
        builder.Property(x => x.SubTotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Tax).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Property(x => x.VisitCreditAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TrackingToken).HasMaxLength(Order.TrackingTokenLength).IsUnicode(false);
        builder.Property(x => x.ReplyChannel).HasConversion<int?>();
        builder.Property(x => x.CustomerResponse).HasMaxLength(QuoteRules.MaxResponseLength);
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.VisitCredit);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        // A customer deleted from the list leaves the quote with the name, phone and email it keeps.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
        // The appointment of the visit and the sale it takes off stay while a quote points to them.
        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.VisitCreditSaleOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.TenantId, x.Number, x.Revision }).IsUnique();
        // One correction at a time: a folio has at most one draft.
        builder.HasIndex(x => new { x.TenantId, x.Number })
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_Quotes_TenantId_Number_Draft");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
        builder.HasIndex(x => new { x.TenantId, x.CustomerPhone });
        builder.HasIndex(x => x.AppointmentId).HasFilter("[AppointmentId] IS NOT NULL");
        builder.HasIndex(x => x.CustomerId).HasFilter("[CustomerId] IS NOT NULL");
        builder.HasIndex(x => x.VisitCreditSaleOrderId).HasFilter("[VisitCreditSaleOrderId] IS NOT NULL");
        builder.HasIndex(x => x.TrackingToken)
            .IsUnique()
            .HasFilter("[TrackingToken] IS NOT NULL")
            .HasDatabaseName("IX_Quotes_TrackingToken");
    }
}

/// <summary>
/// The lines of a version of a quote (Fase 29C). A catalog product deleted since leaves the line with what it says: its
/// description, quantity, price and IVA.
/// </summary>
public class QuoteLineConfiguration : IEntityTypeConfiguration<QuoteLine>
{
    public void Configure(EntityTypeBuilder<QuoteLine> builder)
    {
        builder.ToTable("QuoteLines", table =>
        {
            table.HasCheckConstraint("CK_QuoteLines_Position", $"[Position] BETWEEN 1 AND {QuoteRules.MaxLines}");
            table.HasCheckConstraint("CK_QuoteLines_Quantity", $"[Quantity] > 0 AND [Quantity] <= {QuoteRules.MaxQuantity:0}");
            table.HasCheckConstraint("CK_QuoteLines_UnitPrice", $"[UnitPrice] >= 0 AND [UnitPrice] <= {QuoteRules.MaxUnitPrice:0}");
            table.HasCheckConstraint("CK_QuoteLines_Tax", "[IsTaxExempt] = 0 OR [TaxRate] = 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(QuoteRules.MaxDescriptionLength).IsRequired();
        builder.Property(x => x.Quantity).HasColumnType("decimal(18,3)");
        builder.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TaxRate).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Total).HasColumnType("decimal(18,2)");
        builder.Ignore(x => x.VatRate);

        builder.HasOne<Domain.Catalog.Entities.Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.QuoteId, x.Position }).IsUnique();
        builder.HasIndex(x => x.ProductId).HasFilter("[ProductId] IS NOT NULL");
    }
}
