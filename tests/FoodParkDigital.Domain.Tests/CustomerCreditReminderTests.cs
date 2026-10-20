using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A customer's own term and the reminders of what is overdue (Fase 26C, ADR 0035): the term goes from 1 to 120 days or
/// back to the business's, and the reminders repeat every 1 to 30 days with a template of their own.
/// </summary>
public class CustomerCreditReminderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(120)]
    public void CustomerTerm_ShouldGoFromOneTo120Days_AndBackToTheBusinesss(int days)
    {
        var customer = new Customer(Tenant, "Luis Pérez", "+525512345678", null);

        customer.SetCreditTermDays(days);
        Assert.Equal(days, customer.CreditTermDays);

        customer.SetCreditTermDays(null);
        Assert.Null(customer.CreditTermDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(121)]
    public void CustomerTerm_ShouldRefuse_DaysOutOfRange(int days)
    {
        var customer = new Customer(Tenant, "Luis Pérez", "+525512345678", null);
        customer.SetCreditTermDays(15);

        var refused = Assert.Throws<DomainException>(() => customer.SetCreditTermDays(days));

        Assert.Equal(Customer.CreditTermRangeMessage, refused.Message);
        Assert.Equal(15, customer.CreditTermDays);
    }

    [Fact]
    public void CustomerTerm_ShouldNeed_ARealCustomerWithAPhone_ButNotALimit()
    {
        var walkIn = new Customer(Tenant, "Público en general", null, null, isPublicGeneral: true);
        var noPhone = new Customer(Tenant, "Luis Pérez", null, null);
        var noLimit = new Customer(Tenant, "Luis Pérez", "+525512345678", null);

        Assert.Equal(Customer.PublicGeneralCreditTermMessage, Assert.Throws<DomainException>(() => walkIn.SetCreditTermDays(7)).Message);
        Assert.Equal(Customer.NoPhoneCreditTermMessage, Assert.Throws<DomainException>(() => noPhone.SetCreditTermDays(7)).Message);
        // Orders handed over unpaid are owed without a limit: the term counts for them too.
        noLimit.SetCreditTermDays(7);
        Assert.Equal(7, noLimit.CreditTermDays);
        Assert.Null(noLimit.CreditLimit);
        // Going back to the business's term never needs anything.
        walkIn.SetCreditTermDays(null);
        Assert.Null(walkIn.CreditTermDays);
    }

    [Fact]
    public void CreditReminders_ShouldStartOff_EverySevenDays_WithTheDefaultText()
    {
        var settings = new TenantSettings(Tenant);

        Assert.False(settings.CreditRemindersEnabled);
        Assert.Equal(TenantSettings.DefaultCreditReminderEveryDays, settings.CreditReminderEveryDays);
        Assert.Equal(7, settings.CreditReminderEveryDays);
        Assert.Null(settings.CreditReminderTemplateId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    public void CreditReminders_ShouldRepeatEveryOneTo30Days(int everyDays)
    {
        var settings = new TenantSettings(Tenant);
        var template = Guid.NewGuid();

        settings.UpdateCreditReminders(true, everyDays, template);

        Assert.True(settings.CreditRemindersEnabled);
        Assert.Equal(everyDays, settings.CreditReminderEveryDays);
        Assert.Equal(template, settings.CreditReminderTemplateId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void CreditReminders_ShouldRefuse_ACadenceOutOfRange(int everyDays)
    {
        var settings = new TenantSettings(Tenant);

        var refused = Assert.Throws<DomainException>(() => settings.UpdateCreditReminders(true, everyDays, null));

        Assert.Equal(TenantSettings.CreditReminderEveryDaysMessage, refused.Message);
        Assert.False(settings.CreditRemindersEnabled);
    }

    [Fact]
    public void CreditReminderTemplate_ShouldUseWhatIsOwed_AndNotTheAppointment()
    {
        var template = new MessageTemplate(Tenant, "Saldo vencido", MessagePurpose.CreditReminder,
            "Hola {nombre}, en {negocio} debes {monto}; {vencido} venció el {fecha}.", "saldo_vencido");

        Assert.Equal(MessagePurpose.CreditReminder, template.Purpose);
        Assert.Equal(["nombre", "negocio", "monto", "vencido", "fecha"], MessageTemplateText.GetPlaceholders(template.Body));
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Saldo", MessagePurpose.CreditReminder, "Tu cita de {servicio} a las {hora}"));
        // What is owed is not for campaigns or appointment reminders.
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Promo", MessagePurpose.Campaign, "Debes {monto}"));
        Assert.Throws<DomainException>(() =>
            new MessageTemplate(Tenant, "Cita", MessagePurpose.AppointmentReminder, "Debes {vencido}"));
    }
}
