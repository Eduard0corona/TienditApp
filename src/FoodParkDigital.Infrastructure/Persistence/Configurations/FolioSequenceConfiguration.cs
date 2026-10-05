using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class FolioSequenceConfiguration : IEntityTypeConfiguration<FolioSequence>
{
    public void Configure(EntityTypeBuilder<FolioSequence> builder)
    {
        builder.ToTable("FolioSequences");
        builder.HasKey(x => new { x.TenantId, x.Scope });
        builder.Property(x => x.Scope).HasMaxLength(FolioSequence.MaxScopeLength).IsRequired();
        // Outside SQL Server (tests) the counter is incremented by EF: a concurrent increment fails instead of
        // repeating a folio. SQL Server increments it in one statement (FolioGenerator).
        builder.Property(x => x.LastNumber).IsRequired().IsConcurrencyToken();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
