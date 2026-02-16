using FoodParkDigital.Domain.AiAgents.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class AiAgentConfiguration : IEntityTypeConfiguration<AiAgent>
{
    public void Configure(EntityTypeBuilder<AiAgent> builder)
    {
        builder.ToTable("AiAgents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Personality).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Tone).HasMaxLength(50).IsRequired();

        builder.Property(x => x.BusinessName).HasMaxLength(200);
        builder.Property(x => x.BusinessDescription).HasMaxLength(2000);
        builder.Property(x => x.BusinessProductsAndServices).HasMaxLength(4000);

        builder.Property(x => x.WelcomeMessage).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.FallbackMessage).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ClosingMessage).HasMaxLength(2000).IsRequired();

        builder.Property(x => x.CustomQuestions).HasMaxLength(4000);

        builder.Property(x => x.Model).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Temperature).HasColumnType("decimal(4,2)");
        builder.Property(x => x.MaxTokens).IsRequired();

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
    }
}
