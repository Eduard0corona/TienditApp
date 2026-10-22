using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class PlatformWithholdingCertificateConfiguration : IEntityTypeConfiguration<PlatformWithholdingCertificate>
{
    public void Configure(EntityTypeBuilder<PlatformWithholdingCertificate> builder)
    {
        // What any CFDI de retenciones keeps (Fase 22J), and the first year of the clave 26 (2019), which no rule moves: the
        // other limits of policy (a billion, a period not in the future) live in PlatformWithholdingCertificate. The total may
        // fall short of the ISR plus the IVA by the cents of rounding an XML that writes more than two decimals.
        builder.ToTable("PlatformWithholdingCertificates", table =>
        {
            table.HasCheckConstraint("CK_PlatformWithholdingCertificates_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint(
                "CK_PlatformWithholdingCertificates_Period",
                "[MonthFrom] BETWEEN 1 AND 12 AND [MonthTo] BETWEEN 1 AND 12 AND [MonthFrom] <= [MonthTo] AND [Year] >= 2019");
            table.HasCheckConstraint(
                "CK_PlatformWithholdingCertificates_Amounts",
                "[OperationTotal] >= 0 AND [IsrWithheld] >= 0 AND [VatWithheld] >= 0 AND [TotalWithheld] + 0.02 >= [IsrWithheld] + [VatWithheld]"
                + " AND ([IsrBase] IS NULL OR [IsrBase] >= 0) AND ([VatBase] IS NULL OR [VatBase] >= 0)");
            table.HasCheckConstraint(
                "CK_PlatformWithholdingCertificates_Removed",
                "([RemovedAt] IS NULL AND [RemovalReason] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovalReason] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.Uuid).IsRequired();
        builder.Property(x => x.IssuerRfc).HasMaxLength(13).IsUnicode(false).IsRequired();
        builder.Property(x => x.IssuerName).HasMaxLength(PlatformWithholdingCertificate.MaxIssuerNameLength).IsRequired();
        builder.Property(x => x.ReceiverRfc).HasMaxLength(13).IsUnicode(false).IsRequired();
        builder.Property(x => x.IssuedOn).HasColumnType("datetime2(0)").IsRequired();
        builder.Property(x => x.OperationTotal).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.IsrBase).HasColumnType("decimal(18,2)");
        builder.Property(x => x.IsrWithheld).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.VatBase).HasColumnType("decimal(18,2)");
        builder.Property(x => x.VatWithheld).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.TotalWithheld).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.ReplacesUuid);
        // Two people removing the same one at once: the second finds it already removed (409), and the activity says it once.
        builder.Property(x => x.RemovedAt).IsConcurrencyToken();
        builder.Property(x => x.RemovalReason).HasMaxLength(PlatformWithholdingCertificate.MaxRemovalReasonLength);
        builder.Ignore(x => x.IsRemoved);

        // One CFDI counts once in a business, also when two people upload it at the same time; a removed one can come back.
        builder.HasIndex(x => new { x.TenantId, x.Uuid })
            .IsUnique()
            .HasFilter("[RemovedAt] IS NULL")
            .HasDatabaseName("IX_PlatformWithholdingCertificates_TenantId_Uuid_Active");
        // The reconciliation of a platform's year, month by month.
        builder.HasIndex(x => new { x.TenantId, x.Channel, x.Year, x.MonthTo });
    }
}
