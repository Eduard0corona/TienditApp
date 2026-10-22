using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The notice before the credit comes due (Fase 26E, ADR 0043): off until the owner turns it on, from 1 to 7 days before
/// (two by default), with a template of its own that says what comes due and not what is overdue.
/// </summary>
public class CreditDueSoonTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Notice_ShouldStartOff_TwoDaysBefore_WithTheDefaultText()
    {
        var settings = new TenantSettings(Tenant);

        Assert.False(settings.CreditDueSoonEnabled);
        Assert.Equal(2, settings.CreditDueSoonDaysBefore);
        Assert.Equal(TenantSettings.DefaultCreditDueSoonDaysBefore, settings.CreditDueSoonDaysBefore);
        Assert.Null(settings.CreditDueSoonTemplateId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Notice_ShouldGoOneTo7DaysBefore_IndependentOfTheOverdueReminders(int daysBefore)
    {
        var settings = new TenantSettings(Tenant);
        settings.UpdateCreditReminders(true, 5, null);
        var template = Guid.NewGuid();

        settings.UpdateCreditDueSoon(true, daysBefore, template);

        Assert.Equal((true, daysBefore, (Guid?)template), (settings.CreditDueSoonEnabled, settings.CreditDueSoonDaysBefore, settings.CreditDueSoonTemplateId));
        Assert.Equal((true, 5, (Guid?)null), (settings.CreditRemindersEnabled, settings.CreditReminderEveryDays, settings.CreditReminderTemplateId));

        settings.UpdateCreditDueSoon(false, daysBefore, null);
        Assert.False(settings.CreditDueSoonEnabled);
        Assert.True(settings.CreditRemindersEnabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Notice_ShouldRefuse_DaysBeforeOutOfRange(int daysBefore)
    {
        var settings = new TenantSettings(Tenant);

        var refused = Assert.Throws<DomainException>(() => settings.UpdateCreditDueSoon(true, daysBefore, null));

        Assert.Equal(TenantSettings.CreditDueSoonDaysBeforeMessage, refused.Message);
        Assert.False(settings.CreditDueSoonEnabled);
        Assert.Equal(2, settings.CreditDueSoonDaysBefore);
    }

    [Fact]
    public void NoticeTemplate_ShouldUseWhatComesDue_AndNotWhatIsOverdue()
    {
        var template = new MessageTemplate(Tenant, "Por vencer", MessagePurpose.CreditDueSoon,
            "Hola {nombre}, en {negocio} debes {monto}; {porvencer} vence el {fecha}.", "por_vencer");

        Assert.Equal(MessagePurpose.CreditDueSoon, template.Purpose);
        Assert.Equal(["nombre", "negocio", "monto", "porvencer", "fecha"], MessageTemplateText.GetPlaceholders(template.Body));
        Assert.Equal(["nombre", "negocio", "monto", "porvencer", "fecha"], MessageTemplateText.AllowedPlaceholders(MessagePurpose.CreditDueSoon));
        // Nothing is overdue yet, and it is not about an appointment.
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Por vencer", MessagePurpose.CreditDueSoon, "Ya venció {vencido}"));
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Por vencer", MessagePurpose.CreditDueSoon, "Tu cita de {servicio} a las {hora}"));
        // What comes due is not for the overdue reminder, campaigns or appointment reminders.
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Saldo", MessagePurpose.CreditReminder, "Vence {porvencer}"));
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Promo", MessagePurpose.Campaign, "Vence {porvencer}"));
    }
}
