using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// How each business invoices (Fase 28A, ADR 0045): one profile per business, with the series of its invoices, payment
/// complements (Fase 28C) and credit notes (Fase 28D), what identifies its CSD and whether its customers invoice their
/// tickets online (Fase 28F, ADR 0050). The CSD itself (key and password) is kept by the PAC, never here.
/// </summary>
public class InvoicingProfileConfiguration : IEntityTypeConfiguration<InvoicingProfile>
{
    public void Configure(EntityTypeBuilder<InvoicingProfile> builder)
    {
        builder.ToTable("InvoicingProfiles", table =>
        {
            table.HasCheckConstraint(
                "CK_InvoicingProfiles_Certificate",
                "([CertificateNumber] IS NULL AND [CertificateRfc] IS NULL AND [CertificateValidFrom] IS NULL AND [CertificateValidTo] IS NULL"
                + " AND [CertificateUploadedAt] IS NULL AND [CertificateUploadedByUserId] IS NULL)"
                + " OR ([CertificateNumber] IS NOT NULL AND [CertificateRfc] IS NOT NULL AND [CertificateValidFrom] IS NOT NULL"
                + " AND [CertificateValidTo] > [CertificateValidFrom] AND [CertificateUploadedAt] IS NOT NULL"
                + " AND [CertificateUploadedByUserId] IS NOT NULL)");
            // Self-invoicing is on only with the key of the tickets' codes, and the key is always of its length.
            table.HasCheckConstraint(
                "CK_InvoicingProfiles_SelfInvoicing",
                $"([SelfInvoicing] = 0 OR [SelfInvoiceKey] IS NOT NULL) AND ([SelfInvoiceKey] IS NULL OR DATALENGTH([SelfInvoiceKey]) = {SelfInvoiceCode.KeyLength})");
            // The month's CFDI go to the accountant on their own only with the accountant's email (Fase 28O).
            table.HasCheckConstraint(
                "CK_InvoicingProfiles_Accountant", "[SendsMonthToAccountant] = 0 OR [AccountantEmail] IS NOT NULL");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Serie).HasMaxLength(OrderInvoice.MaxSerieLength).IsUnicode(false).IsRequired();
        // The profiles that were there before the complements (Fase 28C) take «P».
        builder.Property(x => x.PaymentSerie)
            .HasMaxLength(Cfdi.MaxSerieLength)
            .IsUnicode(false)
            .IsRequired()
            .HasDefaultValue(PaymentComplement.DefaultSerie);
        // And the ones before the credit notes (Fase 28D) take «NC».
        builder.Property(x => x.CreditNoteSerie)
            .HasMaxLength(Cfdi.MaxSerieLength)
            .IsUnicode(false)
            .IsRequired()
            .HasDefaultValue(CreditNote.DefaultSerie);
        builder.Property(x => x.CertificateNumber).HasMaxLength(InvoicingProfile.CertificateNumberLength).IsUnicode(false);
        builder.Property(x => x.CertificateRfc).HasMaxLength(13);
        // The profiles that were there before self-invoicing (Fase 28F) have it off and no key.
        builder.Property(x => x.SelfInvoicing).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.SelfInvoiceKey).HasMaxLength(SelfInvoiceCode.KeyLength);
        // Fase 28O: the profiles that were there before have no accountant, and nothing goes on its own.
        builder.Property(x => x.AccountantEmail).HasMaxLength(InvoiceEmailAddress.MaxLength);
        builder.Property(x => x.SendsMonthToAccountant).IsRequired().HasDefaultValue(false);
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
