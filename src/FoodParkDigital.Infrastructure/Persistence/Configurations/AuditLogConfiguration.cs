using FoodParkDigital.Domain.Audit.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.PerformedBy).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Metadata).HasColumnType("nvarchar(max)");
        builder.Property(a => a.TenantId).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.Timestamp }).IsDescending(false, true);
        builder.HasIndex(a => new { a.TenantId, a.EntityName, a.EntityId });
    }
}
