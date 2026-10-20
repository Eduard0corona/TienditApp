using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("TenantSettings", table =>
        {
            table.HasCheckConstraint(
                "CK_TenantSettings_HomeVisitTravelMinutes", "[HomeVisitTravelMinutes] >= 0 AND [HomeVisitTravelMinutes] <= 240");
            table.HasCheckConstraint(
                "CK_TenantSettings_CreditTermDays",
                $"[CreditTermDays] IS NULL OR ([CreditTermDays] >= {TenantSettings.MinCreditTermDays} AND [CreditTermDays] <= {TenantSettings.MaxCreditTermDays})");
            table.HasCheckConstraint(
                "CK_TenantSettings_CreditReminderEveryDays",
                $"[CreditReminderEveryDays] >= {TenantSettings.MinCreditReminderEveryDays} AND [CreditReminderEveryDays] <= {TenantSettings.MaxCreditReminderEveryDays}");
            table.HasCheckConstraint(
                "CK_TenantSettings_OnlineBookingCapacity",
                $"[OnlineBookingCapacity] >= 1 AND [OnlineBookingCapacity] <= {OnlineBookingRules.MaxCapacity}");
        });
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.TimeZone).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Capabilities).HasConversion<int>().IsRequired();
        builder.Property(s => s.SettingsJson).HasColumnType("nvarchar(max)");
        builder.Property(s => s.DefaultTaxRate).HasColumnType("decimal(5,2)");
        builder.Property(s => s.OnlineMenuMode).HasConversion<int>().IsRequired();
        builder.Property(s => s.OrderNotificationPolicy).HasConversion<int>().IsRequired();
        builder.Property(s => s.InsufficientStockPolicy).HasConversion<int>().IsRequired();
        // The database default only fills the rows the previous version saves while this one is published; EF always
        // writes the value.
        builder.Property(s => s.Theme)
            .HasConversion<int>()
            .HasDefaultValue(BusinessTheme.Nopal)
            .ValueGeneratedNever()
            .IsRequired();
        // Fase 22E: the database default fills the rows that exist and those the previous version saves while this one is
        // published; EF always writes the value.
        builder.Property(s => s.HomeVisitTravelMinutes)
            .HasDefaultValue(HomeVisitRules.DefaultTravelMinutes)
            .ValueGeneratedNever()
            .IsRequired();
        // Fase 24A (ADR 0021): every business starts listed in Explorar while its menu is published; the database default
        // fills the rows that exist and those the previous version saves while this one is published. EF always writes the
        // value.
        builder.Property(s => s.ListedInDirectory)
            .HasDefaultValue(true)
            .ValueGeneratedNever()
            .IsRequired();
        // Fase 24D (ADR 0025): online booking starts off, for one appointment at a time; the database defaults fill the rows
        // that exist and those the previous version saves while this one is published. EF always writes the values.
        builder.Property(s => s.OnlineBookingEnabled)
            .HasDefaultValue(false)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(s => s.OnlineBookingCapacity)
            .HasDefaultValue(OnlineBookingRules.DefaultCapacity)
            .ValueGeneratedNever()
            .IsRequired();
        // Fase 26C (ADR 0035): credit reminders start off, every 7 days; the database defaults fill the rows that exist and
        // those the previous version saves while this one is published. EF always writes the values.
        builder.Property(s => s.CreditRemindersEnabled)
            .HasDefaultValue(false)
            .ValueGeneratedNever()
            .IsRequired();
        builder.Property(s => s.CreditReminderEveryDays)
            .HasDefaultValue(TenantSettings.DefaultCreditReminderEveryDays)
            .ValueGeneratedNever()
            .IsRequired();
        // Null until the owner sets the hours: the version this one replaces does not map it.
        builder.Property(s => s.OpeningHours)
            .HasConversion(OpeningHoursStorage.Converter)
            .HasMaxLength(OpeningHoursStorage.MaxLength);

        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}
