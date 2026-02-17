using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class OwnerWhatsAppConfigConfiguration : IEntityTypeConfiguration<OwnerWhatsAppConfig>
{
    public void Configure(EntityTypeBuilder<OwnerWhatsAppConfig> builder)
    {
        builder.ToTable("OwnerWhatsAppConfigs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.UseForOtp).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.AccessToken).HasMaxLength(500);
        builder.Property(x => x.PhoneNumberId).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.WabaId).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.DisplayPhoneNumber).HasMaxLength(20);
        builder.Property(x => x.OtpTemplateName).IsRequired().HasMaxLength(100).HasDefaultValue("otp_verification");
        builder.Property(x => x.OtpTemplateLanguageCode).IsRequired().HasMaxLength(10).HasDefaultValue("es_MX");

        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<OwnerWhatsAppConfig>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
