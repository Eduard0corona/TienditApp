using FoodParkDigital.Domain.Customers.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The balance in a customer's favor (Fase 26D, ADR 0036): movements that are never edited or deleted, whose sum per account is
/// the balance. The base keeps each one with the sign of its kind and with what it is tied to, and lets a payment or a refund
/// move the balance once.
/// </summary>
public class CustomerBalanceEntryConfiguration : IEntityTypeConfiguration<CustomerBalanceEntry>
{
    public void Configure(EntityTypeBuilder<CustomerBalanceEntry> builder)
    {
        builder.ToTable("CustomerBalanceEntries", table =>
        {
            // Deposit (0) and Refund (1) add to the balance; Applied (2) and Returned (3) take from it.
            table.HasCheckConstraint("CK_CustomerBalanceEntries_Amount",
                "([Kind] IN (0, 1) AND [Amount] > 0) OR ([Kind] IN (2, 3) AND [Amount] < 0)");
            // What is left or given back moves through a drawer; paying with it is a payment, and a refund to it a refund.
            table.HasCheckConstraint("CK_CustomerBalanceEntries_Ties",
                "([Kind] IN (0, 3) AND [Method] IS NOT NULL AND [CashSessionId] IS NOT NULL)"
                + " OR ([Kind] = 1 AND [OrderId] IS NOT NULL AND [OrderRefundId] IS NOT NULL)"
                + " OR ([Kind] = 2 AND [OrderId] IS NOT NULL AND [OrderPaymentId] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(CustomerBalanceEntry.MaxPhoneLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Method).HasConversion<int?>();
        builder.Property(x => x.Reference).HasMaxLength(CustomerBalanceEntry.MaxReferenceLength);
        builder.Property(x => x.Notes).HasMaxLength(CustomerBalanceEntry.MaxNotesLength);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(256).IsRequired();

        // The balance of an account and its latest movements, without reading the table.
        builder.HasIndex(x => new { x.TenantId, x.Phone, x.OccurredAt }).IncludeProperties(x => x.Amount);
        builder.HasIndex(x => x.OrderPaymentId).IsUnique().HasFilter("[OrderPaymentId] IS NOT NULL");
        builder.HasIndex(x => x.OrderRefundId).IsUnique().HasFilter("[OrderRefundId] IS NOT NULL");
    }
}
