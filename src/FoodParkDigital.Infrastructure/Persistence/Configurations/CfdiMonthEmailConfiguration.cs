using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// The emails of a month's CFDI to the business's accountant (Fase 28O, ADR 0060): which month, to whom, who asked for it and
/// how it ended. What the domain guarantees is repeated here, so a row the domain never writes is refused.
/// </summary>
public class CfdiMonthEmailConfiguration : IEntityTypeConfiguration<CfdiMonthEmail>
{
    public void Configure(EntityTypeBuilder<CfdiMonthEmail> builder)
    {
        builder.ToTable("CfdiMonthEmails", table =>
        {
            table.HasCheckConstraint(
                "CK_CfdiMonthEmails_Month", $"[Year] >= {CfdiMonthEmail.FirstYear} AND [Month] BETWEEN 1 AND 12");
            // Sent with how many CFDI the list carried, or refused for good with why, never both.
            table.HasCheckConstraint(
                "CK_CfdiMonthEmails_Outcome",
                "([SentAt] IS NULL OR [FailedAt] IS NULL)"
                + " AND (([SentAt] IS NULL AND [CfdiCount] IS NULL) OR ([SentAt] IS NOT NULL AND [CfdiCount] IS NOT NULL AND [CfdiCount] >= 0))"
                + " AND (([FailedAt] IS NULL AND [Failure] IS NULL) OR ([FailedAt] IS NOT NULL AND [Failure] IS NOT NULL AND [Failure] IN (0, 1, 2, 3, 4)))");
            table.HasCheckConstraint(
                "CK_CfdiMonthEmails_Attempts",
                "([FailedAttempts] = 0 AND [LastFailedAttemptAt] IS NULL) OR ([FailedAttempts] > 0 AND [LastFailedAttemptAt] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.To).HasMaxLength(InvoiceEmailAddress.MaxLength).IsRequired();
        builder.Property(x => x.Failure).HasConversion<int?>();
        builder.Ignore(x => x.IsMonthly);
        builder.Ignore(x => x.IsPending);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // The latest emails of a month, and those of a business in the last day.
        builder.HasIndex(x => new { x.TenantId, x.Year, x.Month, x.RequestedAt });
        builder.HasIndex(x => new { x.TenantId, x.RequestedAt });
        // TienditApp sends each month once on its own, even with several instances of the worker.
        builder.HasIndex(x => new { x.TenantId, x.Year, x.Month })
            .IsUnique()
            .HasFilter("[RequestedByUserId] IS NULL")
            .HasDatabaseName("UX_CfdiMonthEmails_Monthly");
    }
}
