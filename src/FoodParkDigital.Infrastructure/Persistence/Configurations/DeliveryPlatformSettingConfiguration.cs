using FoodParkDigital.Domain.Orders.Platforms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class DeliveryPlatformSettingConfiguration : IEntityTypeConfiguration<DeliveryPlatformSetting>
{
    public void Configure(EntityTypeBuilder<DeliveryPlatformSetting> builder)
    {
        // Only the delivery platforms (Rappi 3, Uber Eats 4, DiDi Food 5) and rates from 0 to 100: the limits of policy
        // (a commission up to 60 %, ISR up to 20 %) live in PlatformTerms and can be relaxed without a migration.
        builder.ToTable("DeliveryPlatformSettings", table =>
        {
            table.HasCheckConstraint("CK_DeliveryPlatformSettings_Channel", "[Channel] IN (3, 4, 5)");
            table.HasCheckConstraint("CK_DeliveryPlatformSettings_CommissionBase", "[CommissionBase] IN (0, 1)");
            table.HasCheckConstraint(
                "CK_DeliveryPlatformSettings_Percents",
                "[CommissionPercent] BETWEEN 0 AND 100 AND [CommissionVatPercent] BETWEEN 0 AND 100 AND [IsrWithholdingPercent] BETWEEN 0 AND 100 AND [VatWithholdingPercent] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_DeliveryPlatformSettings_Revision", "[Revision] >= 1");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CommissionPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.CommissionBase).HasConversion<int>().IsRequired();
        builder.Property(x => x.CommissionVatPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.IsrWithholdingPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.VatWithholdingPercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Ignore(x => x.Terms);

        // One row per platform and business, also when two people save the first one at the same time.
        builder.HasIndex(x => new { x.TenantId, x.Channel }).IsUnique();
    }
}
