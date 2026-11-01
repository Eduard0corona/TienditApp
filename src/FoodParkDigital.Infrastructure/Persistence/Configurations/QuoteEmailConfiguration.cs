using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The emails of the quotes to their customers (Fase 29D, ADR 0053 point 16): which version each one carried, to whom, who
/// asked for it and how it ended. What the domain guarantees is repeated here, so a row the domain never writes is refused.
/// </summary>
public class QuoteEmailConfiguration : IEntityTypeConfiguration<QuoteEmail>
{
    public void Configure(EntityTypeBuilder<QuoteEmail> builder)
    {
        builder.ToTable("QuoteEmails", table =>
        {
            table.HasCheckConstraint(
                "CK_QuoteEmails_Outcome",
                "([SentAt] IS NULL OR [FailedAt] IS NULL)"
                + " AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2, 3)))");
            table.HasCheckConstraint(
                "CK_QuoteEmails_Attempts",
                "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.QuoteId).IsRequired();
        builder.Property(x => x.To).HasMaxLength(InvoiceEmailAddress.MaxLength).IsRequired();
        builder.Property(x => x.RequestedByUserId).IsRequired();
        builder.Property(x => x.Failure).HasConversion<int?>();
        builder.Ignore(x => x.IsPending);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        // Quotes are never deleted: the history of their emails stays with them.
        builder.HasOne<Quote>()
            .WithMany()
            .HasForeignKey(x => x.QuoteId)
            .OnDelete(DeleteBehavior.NoAction);

        // The emails of a version (its latest ones, and how many in the last day), and of a business in the last day.
        builder.HasIndex(x => new { x.QuoteId, x.RequestedAt });
        builder.HasIndex(x => new { x.TenantId, x.RequestedAt });
    }
}
