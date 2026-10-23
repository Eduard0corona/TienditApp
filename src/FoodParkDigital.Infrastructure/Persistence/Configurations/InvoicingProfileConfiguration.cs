using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// How each business invoices (Fase 28A, ADR 0045): one profile per business, with the serie and what identifies its CSD.
/// The CSD itself (key and password) is kept by the PAC, never here.
/// </summary>
public class InvoicingProfileConfiguration : IEntityTypeConfiguration<InvoicingProfile>
{
    public void Configure(EntityTypeBuilder<InvoicingProfile> builder)
    {
        builder.ToTable("InvoicingProfiles", table => table.HasCheckConstraint(
            "CK_InvoicingProfiles_Certificate",
            "([CertificateNumber] IS NULL AND [CertificateRfc] IS NULL AND [CertificateValidFrom] IS NULL AND [CertificateValidTo] IS NULL"
            + " AND [CertificateUploadedAt] IS NULL AND [CertificateUploadedByUserId] IS NULL)"
            + " OR ([CertificateNumber] IS NOT NULL AND [CertificateRfc] IS NOT NULL AND [CertificateValidFrom] IS NOT NULL"
            + " AND [CertificateValidTo] > [CertificateValidFrom] AND [CertificateUploadedAt] IS NOT NULL"
            + " AND [CertificateUploadedByUserId] IS NOT NULL)"));
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(OrderInvoice.MaxSerieLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.CertificateNumber).HasMaxLength(InvoicingProfile.CertificateNumberLength).IsUnicode(false);
        builder.Property(x => x.CertificateRfc).HasMaxLength(13);
        builder.Property(x => x.Version).IsRowVersion();
        builder.Ignore(x => x.HasCertificate);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TenantId).IsUnique();
        // The platform's PAC account keeps each CSD once, by its number: one business per CSD, so removing it never stops
        // another business's invoices.
        builder.HasIndex(x => x.CertificateNumber).IsUnique().HasFilter("[CertificateNumber] IS NOT NULL");
    }
}
