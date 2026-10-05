using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TokenHash).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(r => r.TokenHash)
            .IsUnique()
            .HasFilter("[TokenHash] IS NOT NULL");

        // The version running when the migration is applied still inserts tokens without a session: each one
        // becomes its own session.
        builder.Property(r => r.SessionId).HasDefaultValueSql("NEWID()");
        builder.HasIndex(r => r.SessionId);

        // Two renewals with the same token: only the first one replaces it.
        builder.Property(r => r.RevokedAt).IsConcurrencyToken();

        // Plain values of the tokens issued before they were hashed. The new version only empties them; they go
        // away with their columns once no version writes them.
        builder.Property(r => r.LegacyValue).HasColumnName("Token").HasMaxLength(500);
        builder.Property(r => r.LegacyReplacementValue).HasColumnName("ReplacedByToken").HasMaxLength(500);
        builder.HasIndex(r => r.LegacyValue).HasDatabaseName("IX_RefreshTokens_Token");

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
