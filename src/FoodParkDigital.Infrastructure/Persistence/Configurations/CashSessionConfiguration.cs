using FoodParkDigital.Domain.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("CashSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.OpenedAt).IsRequired();
        builder.Property(x => x.OpenedBy).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OpeningAmount).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.ClosedAt);
        builder.Property(x => x.ClosedBy).HasMaxLength(200);
        builder.Property(x => x.ClosingAmount).HasColumnType("decimal(18,4)");
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.OpenedAt });

        builder.HasMany(x => x.Transactions)
            .WithOne(x => x.CashSession)
            .HasForeignKey(x => x.CashSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Transactions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
