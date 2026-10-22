using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.Support.CreditTestKit;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Plazo por cliente y recordatorios del fiado (Fase 26C, ADR 0035): a customer's own term decides what of theirs is
/// overdue instead of the business's, and the business reminds on WhatsApp what each customer owes past their term, during
/// its day, every few days, never to who wrote BAJA, and not once they paid.
/// </summary>
public class CustomerCreditReminderFlowTests
{
    private const string AnaPhone = "+525512345678";
    private const string LuisPhone = "+525566667777";
    private const string EvaPhone = "+525588889999";
    // Now is Sunday 4 Oct at 11:00 in Mexico City.
    private static readonly DateTimeOffset TenDaysAgo = new(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(-6));

    [Fact]
    public async Task OwnTerm_ShouldDecideWhatIsOverdue_InPorCobrar_AndTheAccount_InsteadOfTheBusinesss()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 15);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m, termDays: 7);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", LuisPhone, creditLimit: 1000m);
        // Both sold on 24 Sep: Ana's 7 days ended on 1 Oct; Luis's 15 days end on 9 Oct.
        await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: TenDaysAgo);
        await SellOnCreditAsync(fixture, luis, soap, quantity: 1, createdAt: TenDaysAgo);

        var receivables = await ReceivablesAsync(fixture, new GetReceivablesQuery());
        var overdueOnly = await ReceivablesAsync(fixture, new GetReceivablesQuery { OverdueOnly = true });
        var anaAccount = await AccountAsync(fixture, ana.Id);
        var luisAccount = await AccountAsync(fixture, luis.Id);

        Assert.Equal((300m, 200m, (int?)15), (receivables.TotalOwed, receivables.TotalOverdue, receivables.CreditTermDays));
        Assert.Equal(
            [("Ana López", 200m, (int?)7), ("Luis Pérez", 0m, (int?)null)],
            receivables.Page.Items.Select(r => (r.Name, r.Overdue, r.OwnCreditTermDays)));
        Assert.Equal("Ana López", Assert.Single(overdueOnly.Page.Items).Name);
        Assert.Equal((7, (int?)7, 200m), (anaAccount.CreditTermDays, anaAccount.OwnCreditTermDays, anaAccount.Overdue));
        Assert.Equal((new DateOnly(2026, 10, 1), true), (anaAccount.Orders[0].DueOn!.Value, anaAccount.Orders[0].IsOverdue));
        Assert.Equal((15, (int?)null, 0m), (luisAccount.CreditTermDays, luisAccount.OwnCreditTermDays, luisAccount.Overdue));
        Assert.Equal((new DateOnly(2026, 10, 9), false), (luisAccount.Orders[0].DueOn!.Value, luisAccount.Orders[0].IsOverdue));

        // Without a term of the business, a customer's own term still counts.
        await SetBusinessTermAsync(fixture, null);
        var withoutBusinessTerm = await ReceivablesAsync(fixture, new GetReceivablesQuery());
        Assert.Equal((200m, (int?)null), (withoutBusinessTerm.TotalOverdue, withoutBusinessTerm.CreditTermDays));
        Assert.Equal((7, 200m), ((await AccountAsync(fixture, ana.Id)).CreditTermDays!.Value, (await AccountAsync(fixture, ana.Id)).Overdue));
        Assert.Equal(((int?)null, 0m), ((await AccountAsync(fixture, luis.Id)).CreditTermDays, (await AccountAsync(fixture, luis.Id)).Overdue));
    }

    [Fact]
    public async Task OwnTerm_ShouldBeTheShortest_WhenTwoRecordsShareThePhone_EvenOneSavedWithTenDigits()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m, termDays: 7);
        var old = await AddCustomerAsync(fixture, tenant, "Ana (vieja)", "5512345678", termDays: 3);
        old.SetActive(false);
        await fixture.Context.SaveChangesAsync();
        // Sold 6 days ago: overdue by 3 days, not by 7.
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now.AddDays(-6));

        var account = await AccountAsync(fixture, ana.Id);
        var receivables = await ReceivablesAsync(fixture, new GetReceivablesQuery());

        Assert.Equal((3, (int?)3, 100m), (account.CreditTermDays, account.OwnCreditTermDays, account.Overdue));
        Assert.Equal((100m, (int?)3), (receivables.TotalOverdue, Assert.Single(receivables.Page.Items).OwnCreditTermDays));
    }

    [Fact]
    public async Task SetCustomerCreditTerm_ShouldBeTheOwnersDecision_Recorded_AndGoBackToTheBusinesss()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var walkIn = new Customer(tenant.Id, "Público en general", null, null, isPublicGeneral: true);
        fixture.Context.Customers.Add(walkIn);
        await fixture.Context.SaveChangesAsync();
        var handler = new SetCustomerCreditTermCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var set = await handler.Handle(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = 7 }, default);
        var again = await handler.Handle(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = 7 }, default);
        var back = await handler.Handle(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = null }, default);
        var refused = await handler.Handle(new SetCustomerCreditTermCommand { CustomerId = walkIn.Id, CreditTermDays = 7 }, default);

        Assert.Equal((7, 7, (int?)null), (set.Value.CreditTermDays, again.Value.CreditTermDays, back.Value.CreditTermDays));
        Assert.Equal(Customer.PublicGeneralCreditTermMessage, refused.Error);
        Assert.Null((await fixture.Context.Customers.SingleAsync(c => c.Id == ana.Id)).CreditTermDays);
        var logs = await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerCreditTermUpdated").OrderBy(l => l.Metadata).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal((nameof(Customer), ana.Id.ToString()), (log.EntityName, log.EntityId)));
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new SetCustomerCreditTermCommand { CustomerId = Guid.NewGuid(), CreditTermDays = 7 }, default));
        Assert.True(typeof(SetCustomerCreditTermCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));

        var validator = new SetCustomerCreditTermCommandValidator();
        Assert.Equal(Customer.CreditTermRangeMessage,
            Assert.Single(validator.Validate(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = 121 }).Errors).ErrorMessage);
        Assert.False(validator.Validate(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = 0 }).IsValid);
        Assert.True(validator.Validate(new SetCustomerCreditTermCommand { CustomerId = ana.Id, CreditTermDays = null }).IsValid);
    }

    [Fact]
    public async Task CreditReminders_ShouldNeedWhatsApp_AndAnActiveCreditTemplate_Recorded()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var campaign = new MessageTemplate(tenant.Id, "Promo", MessagePurpose.Campaign, "Hola {nombre}");
        var retired = new MessageTemplate(tenant.Id, "Vieja", MessagePurpose.CreditReminder, "Debes {vencido}");
        retired.SetActive(false);
        var credit = new MessageTemplate(tenant.Id, "Saldo vencido", MessagePurpose.CreditReminder,
            "Hola {nombre}, debes {vencido} desde el {fecha}.", "saldo_vencido");
        fixture.Context.MessageTemplates.AddRange(campaign, retired, credit);
        await fixture.Context.SaveChangesAsync();
        var handler = UpdateRemindersHandler(fixture);

        var withoutWhatsApp = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = true, EveryDays = 5 }, default);
        await ConnectWhatsAppAsync(fixture, tenant);
        var campaignTemplate = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = true, TemplateId = campaign.Id }, default);
        var retiredTemplate = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = true, TemplateId = retired.Id }, default);
        var on = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = true, EveryDays = 5, TemplateId = credit.Id }, default);
        var again = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = true, EveryDays = 5, TemplateId = credit.Id }, default);

        Assert.Equal(MessagingDefaults.WhatsAppNotConfigured, withoutWhatsApp.Error);
        Assert.Equal(UpdateCreditRemindersCommandHandler.TemplateNotFoundMessage, campaignTemplate.Error);
        Assert.Equal(UpdateCreditRemindersCommandHandler.TemplateNotFoundMessage, retiredTemplate.Error);
        Assert.True(on.IsSuccess, on.Error);
        Assert.Equal((true, 5, (Guid?)credit.Id, true), (on.Value.Enabled, on.Value.EveryDays, on.Value.TemplateId, on.Value.WhatsAppConnected));
        Assert.Equal("Saldo vencido", Assert.Single(on.Value.Templates).Name);
        Assert.Equal(["nombre", "negocio", "monto", "vencido", "fecha"], on.Value.Placeholders);
        Assert.Equal((MessagingDefaults.CreditReminderBody, "10:00", "19:00", 1, 30),
            (on.Value.DefaultBody, on.Value.SendFrom, on.Value.SendUntil, on.Value.MinEveryDays, on.Value.MaxEveryDays));
        Assert.True(again.IsSuccess);
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CreditRemindersUpdated").ToListAsync());
        Assert.Contains("\"EveryDays\":5", log.Metadata);
        // A template retired after it was chosen reads as the default text, which the reminders send instead: the screen
        // sends that back, and the owner can still save.
        credit.SetActive(false);
        await fixture.Context.SaveChangesAsync();
        var retiredChoice = await new GetCreditRemindersQueryHandler(fixture.Context, fixture.TenantService).Handle(new GetCreditRemindersQuery(), default);
        Assert.Null(retiredChoice.TemplateId);
        Assert.Empty(retiredChoice.Templates);
        // Turning them off needs no WhatsApp: a business that disconnected it can stop them.
        fixture.Context.WhatsAppConfigs.RemoveRange(fixture.Context.WhatsAppConfigs);
        await fixture.Context.SaveChangesAsync();
        var off = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = false, EveryDays = 5, TemplateId = retiredChoice.TemplateId }, default);
        Assert.True(off.IsSuccess, off.Error);
        Assert.False(off.Value.Enabled);
        Assert.True(typeof(UpdateCreditRemindersCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
        Assert.True(typeof(GetCreditRemindersQuery).IsDefined(typeof(RequiresTenantManagerAttribute), false));
        Assert.False(new UpdateCreditRemindersCommandValidator().Validate(new UpdateCreditRemindersCommand { EveryDays = 31 }).IsValid);
    }

    [Fact]
    public async Task Planner_ShouldQueueOneReminderPerCustomerWithSomethingOverdue_WithWhatTheyOwe_ThenWaitTheDays()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnRemindersOnAsync(fixture, tenant, everyDays: 3);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", LuisPhone, creditLimit: 1000m);
        // Ana owes $300: $200 sold on 26 Sep (its last day to pay was 3 Oct) and $100 today. Luis owes nothing overdue.
        await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(-6)));
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        await SellOnCreditAsync(fixture, luis, soap, quantity: 1, createdAt: SalesTestFixture.Now.AddDays(-1));

        var queued = await QueueAsync(fixture);

        Assert.Equal(1, queued);
        var message = Assert.Single(await fixture.Context.OutgoingMessages.ToListAsync());
        Assert.Equal(
            (MessagePurpose.CreditReminder, AnaPhone, "Ana López", "credit:+525512345678:20261004", (string?)null, SalesTestFixture.Now),
            (message.Purpose, message.Phone, message.RecipientName, message.DeduplicationKey, message.ProviderTemplateName, message.NextAttemptAt));
        Assert.Equal(
            $"Hola Ana, te recordamos que en {tenant.Name} tienes un saldo de $300.00; $200.00 venció el sábado 3 de octubre. "
            + "Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.",
            message.Body);

        // Once a day, then every 3 days while it stays overdue.
        Assert.Equal(0, await QueueAsync(fixture));
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(2);
        Assert.Equal(0, await QueueAsync(fixture));
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(3);
        Assert.Equal(1, await QueueAsync(fixture));
        Assert.Equal("credit:+525512345678:20261007", (await fixture.Context.OutgoingMessages.OrderByDescending(m => m.CreatedAt).FirstAsync()).DeduplicationKey);
    }

    [Fact]
    public async Task Planner_ShouldReachEveryAccountPastTheLimitOfAPass_AndCountOnlyRemindersTheCustomerCouldGet()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnRemindersOnAsync(fixture, tenant, everyDays: 7);
        // All three owe something overdue: Ana the most, then Luis, then Eva. Ana wrote BAJA.
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", LuisPhone, creditLimit: 1000m);
        var eva = await AddCustomerAsync(fixture, tenant, "Eva Ruiz", EvaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 3, createdAt: TenDaysAgo);
        await SellOnCreditAsync(fixture, luis, soap, quantity: 2, createdAt: TenDaysAgo);
        await SellOnCreditAsync(fixture, eva, soap, quantity: 1, createdAt: TenDaysAgo);
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, AnaPhone, OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();

        // One per pass: who wrote BAJA or was reminded does not take the place of the rest.
        Assert.Equal(1, await QueueAsync(fixture, passLimit: 1));
        Assert.Equal(1, await QueueAsync(fixture, passLimit: 1));
        Assert.Equal(0, await QueueAsync(fixture, passLimit: 1));
        var reminders = await fixture.Context.OutgoingMessages.ToListAsync();
        Assert.Equal([LuisPhone, EvaPhone], reminders.Select(m => m.Phone).Order());

        // The next day: Luis's was skipped because the reminders were off, which he never got, so he is reminded again;
        // Eva's failed to reach her, and she waits the days.
        reminders.Single(m => m.Phone == LuisPhone).Skip(OutgoingMessageSender.CreditRemindersOffReason);
        reminders.Single(m => m.Phone == EvaPhone).Fail("WhatsApp no lo entregó.");
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(1, await QueueAsync(fixture));
        Assert.Equal("credit:+525566667777:20261005",
            (await fixture.Context.OutgoingMessages.OrderByDescending(m => m.CreatedAt).FirstAsync()).DeduplicationKey);
    }

    [Fact]
    public async Task Planner_ShouldWait_ForTheBusinesssDay_ForWhatsApp_AndNeverRemindWhoWroteBaja()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: TenDaysAgo);

        // Off until the owner turns them on.
        Assert.Equal(0, await QueueAsync(fixture));
        await TurnRemindersOnAsync(fixture, tenant, everyDays: 7);
        // 9:59 and 19:00 in Mexico City: outside the business's day.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 4, 15, 59, 0, TimeSpan.Zero);
        Assert.Equal(0, await QueueAsync(fixture));
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, await QueueAsync(fixture));
        // Without WhatsApp, none is queued to fail.
        fixture.Clock.UtcNow = SalesTestFixture.Now;
        fixture.Context.WhatsAppConfigs.RemoveRange(fixture.Context.WhatsAppConfigs);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(0, await QueueAsync(fixture));
        await ConnectWhatsAppAsync(fixture, tenant);
        // Ana wrote BAJA.
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, "+5215512345678", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(0, await QueueAsync(fixture));
        Assert.Empty(await fixture.Context.OutgoingMessages.ToListAsync());

        (await fixture.Context.MessagingOptOuts.SingleAsync()).Resubscribe(SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(1, await QueueAsync(fixture));
    }

    [Fact]
    public async Task Planner_ShouldUseTheTemplateApprovedInMeta_WithItsParameters()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var template = new MessageTemplate(tenant.Id, "Saldo vencido", MessagePurpose.CreditReminder,
            "Hola {nombre}, debes {vencido} desde el {fecha} en {negocio}.", "saldo_vencido");
        fixture.Context.MessageTemplates.Add(template);
        await fixture.Context.SaveChangesAsync();
        await TurnRemindersOnAsync(fixture, tenant, everyDays: 7, template.Id);
        // Only Ana has a term (5 days): sold 10 days ago, her last day to pay was 29 Sep.
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m, termDays: 5);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: TenDaysAgo);

        Assert.Equal(1, await QueueAsync(fixture));

        var message = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.Equal(("saldo_vencido", "es_MX"), (message.ProviderTemplateName, message.LanguageCode));
        Assert.Equal(["Ana", "$100.00", "martes 29 de septiembre", tenant.Name], message.GetParameters());
    }

    [Fact]
    public async Task Sender_ShouldSkipAReminder_OncePaidOrForgiven_TurnedOff_OrLate_AndSendItOtherwise()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnRemindersOnAsync(fixture, tenant, everyDays: 1);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: TenDaysAgo);
        Assert.Equal(1, await QueueAsync(fixture));
        var whatsApp = new FakeWhatsApp();
        var sender = new OutgoingMessageSender(fixture.Context, whatsApp,
            new MessagingConsentService(fixture.Context, fixture.TenantService, fixture.Clock), fixture.Clock);
        var reminder = await fixture.Context.OutgoingMessages.SingleAsync();

        // Late: the next day, or after 20:00 of its day.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(OutgoingMessageSender.CreditReminderLateReason, (await sender.SendAsync(reminder, default)).Reason);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(OutgoingMessageSender.CreditReminderLateReason, (await sender.SendAsync(reminder, default)).Reason);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);

        var sent = await sender.SendAsync(reminder, default);
        Assert.Equal(OutgoingSendDecision.Sent, sent.Decision);
        Assert.Equal(("text", AnaPhone), Assert.Single(whatsApp.Sent));

        // Turned off since it was queued.
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateCreditReminders(false, 1, null);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(OutgoingMessageSender.CreditRemindersOffReason, (await sender.SendAsync(reminder, default)).Reason);
        settings.UpdateCreditReminders(true, 1, null);
        await fixture.Context.SaveChangesAsync();

        // Forgiven since it was queued: nothing overdue to remind.
        var order = await fixture.Context.Orders.Include(o => o.WriteOffs).SingleAsync(o => o.Id == sale.Id);
        order.WriteOff(order.Balance, OrderWriteOffReason.Courtesy, null, SalesTestFixture.Now, "dueno");
        await fixture.Context.SaveChangesAsync();
        var skipped = await sender.SendAsync(reminder, default);
        Assert.Equal((OutgoingSendDecision.Skipped, OutgoingMessageSender.NothingOverdueReason), (skipped.Decision, skipped.Reason));
        Assert.Single(whatsApp.Sent);
    }

    private static async Task TurnRemindersOnAsync(SalesTestFixture fixture, Tenant tenant, int everyDays, Guid? templateId = null)
    {
        await ConnectWhatsAppAsync(fixture, tenant);
        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditReminders(true, everyDays, templateId);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>One pass of the planner, as the worker runs it, saved.</summary>
    private static async Task<int> QueueAsync(SalesTestFixture fixture, int passLimit = CreditReminderPlanner.MaxPerPass)
    {
        var settings = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync();
        var queued = await new CreditReminderPlanner(fixture.Context, new PhoneNormalizationService(), fixture.Clock) { PassLimit = passLimit }
            .QueueDueAsync(settings, default);
        await SaveQueuedAsync(fixture);
        return queued;
    }
}
