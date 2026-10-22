using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Customers.Entities;
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
/// El aviso antes de vencer del fiado (Fase 26E, ADR 0043): a few days before something comes due, the business tells each
/// customer who is up to date what comes due and when, once per last day to pay and at most once a day, never the day of the
/// sale, never to who has something overdue or wrote BAJA, and not once they paid it.
/// </summary>
public class CreditDueSoonFlowTests
{
    private const string AnaPhone = "+525512345678";
    private const string LuisPhone = "+525566667777";
    private const string EvaPhone = "+525588889999";
    private const string PedroPhone = "+525533334444";

    // Now is Sunday 4 Oct at 11:00 in Mexico City; with 7 days to pay, what was sold on 27 to 29 Sep comes due by Tuesday 6.
    private static DateTimeOffset SoldOn(int month, int day) => new(2026, month, day, 12, 0, 0, TimeSpan.FromHours(-6));

    [Fact]
    public async Task Planner_ShouldTellWhoIsUpToDate_WhatComesDueSoon_OncePerLastDayToPay_AndAtMostOnceADay()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 2);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", LuisPhone, creditLimit: 1000m);
        var eva = await AddCustomerAsync(fixture, tenant, "Eva Ruiz", EvaPhone, creditLimit: 1000m, termDays: 1);
        var pedro = await AddCustomerAsync(fixture, tenant, "Pedro Gómez", PedroPhone, creditLimit: 1000m);
        // Ana owes $400: $200 due Monday 5, $100 due Tuesday 6 and $100 sold today.
        var first = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SoldOn(9, 28));
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 29));
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        // Luis's came due on Friday 2: his is the overdue reminder. Eva's (1 day) comes due tomorrow, but she bought it today.
        // Pedro's comes due on Wednesday 7, past the two days.
        await SellOnCreditAsync(fixture, luis, soap, quantity: 1, createdAt: SoldOn(9, 25));
        await SellOnCreditAsync(fixture, eva, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        await SellOnCreditAsync(fixture, pedro, soap, quantity: 1, createdAt: SoldOn(9, 30));

        Assert.Equal(1, await QueueAsync(fixture));

        var message = Assert.Single(await fixture.Context.OutgoingMessages.ToListAsync());
        Assert.Equal(
            (MessagePurpose.CreditDueSoon, AnaPhone, "Ana López", "creditsoon:+525512345678:20261005:20261004", (string?)null, SalesTestFixture.Now),
            (message.Purpose, message.Phone, message.RecipientName, message.DeduplicationKey, message.ProviderTemplateName, message.NextAttemptAt));
        Assert.Equal(
            $"Hola Ana, te recordamos que en {tenant.Name} tienes un saldo de $400.00; $200.00 vence el lunes 5 de octubre. "
            + "Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.",
            message.Body);
        Assert.Equal(0, await QueueAsync(fixture));

        // Forgiven what came due Monday, what comes due Tuesday waits for tomorrow: one notice a day.
        await ForgiveAsync(fixture, first.Id);
        Assert.Equal(0, await QueueAsync(fixture));

        // Monday: Ana hears of Tuesday's, Eva of hers (today, the day after she bought it), Pedro of Wednesday's.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(3, await QueueAsync(fixture));
        var monday = await fixture.Context.OutgoingMessages.Where(m => m.CreatedAt == fixture.Clock.UtcNow).ToListAsync();
        Assert.Equal(
            ["creditsoon:+525512345678:20261006:20261005", "creditsoon:+525533334444:20261007:20261005", "creditsoon:+525588889999:20261005:20261005"],
            monday.Select(m => m.DeduplicationKey).Order());
        Assert.Equal(
            $"Hola Ana, te recordamos que en {tenant.Name} tienes un saldo de $200.00; $100.00 vence el martes 6 de octubre. "
            + "Si ya pagaste, ignora este mensaje. Escribe BAJA si no quieres recibir recordatorios.",
            monday.Single(m => m.Phone == AnaPhone).Body);
        Assert.Equal(0, await QueueAsync(fixture));
        // Tuesday: Ana's and Pedro's last days were told already; Eva's became overdue, and is the reminder's.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(2);
        Assert.Equal(0, await QueueAsync(fixture));
    }

    [Fact]
    public async Task Planner_ShouldReachEveryAccountPastTheLimitOfAPass_AndCountOnlyNoticesTheCustomerCouldGet()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 3);
        // All three have something due Monday 5. Ana wrote BAJA.
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", LuisPhone, creditLimit: 1000m);
        var eva = await AddCustomerAsync(fixture, tenant, "Eva Ruiz", EvaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 3, createdAt: SoldOn(9, 28));
        await SellOnCreditAsync(fixture, luis, soap, quantity: 2, createdAt: SoldOn(9, 28));
        await SellOnCreditAsync(fixture, eva, soap, quantity: 1, createdAt: SoldOn(9, 28));
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, AnaPhone, OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();

        // One per pass: who wrote BAJA or was told does not take the place of the rest.
        Assert.Equal(1, await QueueAsync(fixture, passLimit: 1));
        Assert.Equal(1, await QueueAsync(fixture, passLimit: 1));
        Assert.Equal(0, await QueueAsync(fixture, passLimit: 1));
        var notices = await fixture.Context.OutgoingMessages.ToListAsync();
        Assert.Equal([LuisPhone, EvaPhone], notices.Select(m => m.Phone).Order());

        // The next day: Luis's was skipped because the notice was off, which he never got, so he is told again; Eva's
        // failed to reach her, and counts.
        notices.Single(m => m.Phone == LuisPhone).Skip(OutgoingMessageSender.CreditDueSoonOffReason);
        notices.Single(m => m.Phone == EvaPhone).Fail("WhatsApp no lo entregó.");
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(1, await QueueAsync(fixture));
        Assert.Equal("creditsoon:+525566667777:20261005:20261005",
            (await fixture.Context.OutgoingMessages.OrderByDescending(m => m.CreatedAt).FirstAsync()).DeduplicationKey);
    }

    [Fact]
    public async Task Planner_ShouldWait_ForTheNotice_TheBusinesssDay_WhatsApp_AndATerm_AndNeverTellWhoWroteBaja()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 28));
        // The overdue reminders on do not turn the notice on.
        await ConnectWhatsAppAsync(fixture, tenant);
        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditReminders(true, 7, null);
        await fixture.Context.SaveChangesAsync();
        await SetBusinessTermAsync(fixture, 7);
        Assert.Equal(0, await QueueAsync(fixture));

        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditDueSoon(true, 2, null);
        await fixture.Context.SaveChangesAsync();
        // Without a term nothing comes due.
        await SetBusinessTermAsync(fixture, null);
        Assert.Equal(0, await QueueAsync(fixture));
        await SetBusinessTermAsync(fixture, 7);
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
        // Ana wrote BAJA, from the phone stored with the 1.
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(tenant.Id, "+5215512345678", OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(0, await QueueAsync(fixture));
        Assert.Empty(await fixture.Context.OutgoingMessages.ToListAsync());

        (await fixture.Context.MessagingOptOuts.SingleAsync()).Resubscribe(SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(1, await QueueAsync(fixture));
    }

    [Fact]
    public async Task Planner_ShouldUseItsTemplateApprovedInMeta_WithItsParameters_AndNeverTheOverdueReminders()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var reminder = new MessageTemplate(tenant.Id, "Saldo vencido", MessagePurpose.CreditReminder, "Debes {vencido}.", "saldo_vencido");
        var notice = new MessageTemplate(tenant.Id, "Por vencer", MessagePurpose.CreditDueSoon,
            "Hola {nombre}, el {fecha} vence {porvencer} en {negocio}.", "por_vencer");
        fixture.Context.MessageTemplates.AddRange(reminder, notice);
        await fixture.Context.SaveChangesAsync();
        // Only Ana has a term (6 days): sold on 29 Sep, her last day to pay is Monday 5.
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m, termDays: 6);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 29));

        // A template of the overdue reminders is not the notice's: the default text goes instead.
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 1, reminder.Id);
        Assert.Equal(1, await QueueAsync(fixture));
        var withDefault = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.Null(withDefault.ProviderTemplateName);
        Assert.StartsWith("Hola Ana, te recordamos", withDefault.Body);

        fixture.Context.OutgoingMessages.Remove(withDefault);
        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditDueSoon(true, 1, notice.Id);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(1, await QueueAsync(fixture));
        var message = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.Equal(("por_vencer", "es_MX"), (message.ProviderTemplateName, message.LanguageCode));
        Assert.Equal(["Ana", "lunes 5 de octubre", "$100.00", tenant.Name], message.GetParameters());
    }

    [Fact]
    public async Task Sender_ShouldSkipANotice_OncePaidForgivenOrItsDayMoved_TurnedOff_OrLate_AndSendItOtherwise()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 2);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 28));
        Assert.Equal(1, await QueueAsync(fixture));
        var whatsApp = new FakeWhatsApp();
        var sender = new OutgoingMessageSender(fixture.Context, whatsApp,
            new MessagingConsentService(fixture.Context, fixture.TenantService, fixture.Clock), fixture.Clock);
        var notice = await fixture.Context.OutgoingMessages.SingleAsync();

        // Late: the next day, or after 20:00 of its day.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(OutgoingMessageSender.CreditDueSoonLateReason, (await sender.SendAsync(notice, default)).Reason);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(OutgoingMessageSender.CreditDueSoonLateReason, (await sender.SendAsync(notice, default)).Reason);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);

        var sent = await sender.SendAsync(notice, default);
        Assert.Equal(OutgoingSendDecision.Sent, sent.Decision);
        Assert.Equal(("text", AnaPhone), Assert.Single(whatsApp.Sent));

        // Turned off since it was queued.
        var settings = await fixture.Context.TenantSettings.SingleAsync();
        settings.UpdateCreditDueSoon(false, 2, null);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(OutgoingMessageSender.CreditDueSoonOffReason, (await sender.SendAsync(notice, default)).Reason);
        settings.UpdateCreditDueSoon(true, 2, null);
        await fixture.Context.SaveChangesAsync();

        // A longer term since it was queued: nothing comes due that day anymore.
        await SetBusinessTermAsync(fixture, 10);
        Assert.Equal(OutgoingMessageSender.NothingDueSoonReason, (await sender.SendAsync(notice, default)).Reason);
        await SetBusinessTermAsync(fixture, 7);

        // A shorter term of her own since it was queued: something is overdue, which is the overdue reminder's.
        var record = await fixture.Context.Customers.SingleAsync(c => c.Id == ana.Id);
        record.SetCreditTermDays(3);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(OutgoingMessageSender.OverdueInsteadReason, (await sender.SendAsync(notice, default)).Reason);
        record.SetCreditTermDays(null);
        await fixture.Context.SaveChangesAsync();

        // Ana wrote BAJA: like every reminder, it stops.
        var optOut = new MessagingOptOut(tenant.Id, AnaPhone, OptOutSource.CustomerKeyword, SalesTestFixture.Now);
        fixture.Context.MessagingOptOuts.Add(optOut);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(OutgoingMessageSender.OptedOutReason, (await sender.SendAsync(notice, default)).Reason);
        optOut.Resubscribe(SalesTestFixture.Now);
        await fixture.Context.SaveChangesAsync();

        // Forgiven since it was queued: nothing comes due to tell.
        await ForgiveAsync(fixture, sale.Id);
        var skipped = await sender.SendAsync(notice, default);
        Assert.Equal((OutgoingSendDecision.Skipped, OutgoingMessageSender.NothingDueSoonReason), (skipped.Decision, skipped.Reason));
        Assert.Single(whatsApp.Sent);
    }

    [Fact]
    public async Task PlannerAndSender_ShouldReadOnlyTheirBusiness_WhateverAnotherDidWithTheSamePhone()
    {
        await using var fixture = SalesTestFixture.Create();
        // In another business the same phone owes something overdue, wrote BAJA and was told of Monday already.
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true, slug: "otro");
        var otherSoap = await fixture.AddProductAsync(other, "Jabón", 100m);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, other, daysBefore: 2);
        var otherAna = await AddCustomerAsync(fixture, other, "Ana López", AnaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, otherAna, otherSoap, quantity: 1, createdAt: SoldOn(9, 20));
        fixture.Context.MessagingOptOuts.Add(new MessagingOptOut(other.Id, AnaPhone, OptOutSource.CustomerKeyword, SalesTestFixture.Now));
        fixture.Context.OutgoingMessages.Add(new OutgoingMessage(other.Id, MessagePurpose.CreditDueSoon, AnaPhone, "Ana", "Hola", null,
            "es_MX", [], SalesTestFixture.Now, CreditDueSoonPlanner.Key(AnaPhone, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 4))));
        await fixture.Context.SaveChangesAsync();

        // In this one Ana is up to date and owes something due Monday.
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 2);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 28));

        Assert.Equal(1, await QueueAsync(fixture));
        var notice = await fixture.Context.OutgoingMessages.SingleAsync();
        Assert.Equal((tenant.Id, "creditsoon:+525512345678:20261005:20261004"), (notice.TenantId, notice.DeduplicationKey));
        var sender = new OutgoingMessageSender(fixture.Context, new FakeWhatsApp(),
            new MessagingConsentService(fixture.Context, fixture.TenantService, fixture.Clock), fixture.Clock);
        Assert.Equal(OutgoingSendDecision.Sent, (await sender.SendAsync(notice, default)).Decision);
    }

    [Theory]
    [InlineData("creditsoon:+525512345678:20261005:20261004", "+525512345678", "2026-10-05", "2026-10-04")]
    [InlineData("creditsoon:a:b:20261005:20261004", "a:b", "2026-10-05", "2026-10-04")]
    public void Key_ShouldBeReadFromTheRight_AsItWasWritten(string key, string phone, string dueOn, string day)
    {
        Assert.Equal(key, CreditDueSoonPlanner.Key(phone, DateOnly.Parse(dueOn), DateOnly.Parse(day)));
        Assert.True(CreditDueSoonPlanner.TryParseKey(key, out var readPhone, out var readDueOn, out var readDay));
        Assert.Equal((phone, DateOnly.Parse(dueOn), DateOnly.Parse(day)), (readPhone, readDueOn, readDay));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("credit:+525512345678:20261004")]
    [InlineData("creditsoon:+525512345678:20261004")]
    [InlineData("creditsoon::20261005:20261004")]
    [InlineData("creditsoon:+525512345678:2026105:20261004")]
    [InlineData("creditsoon:")]
    public void Key_ShouldNotBeRead_WhenItIsNotANoticesKey(string? key)
        => Assert.False(CreditDueSoonPlanner.TryParseKey(key, out _, out _, out _));

    [Fact]
    public async Task Settings_ShouldTurnTheNoticeOn_WithWhatsApp_AndItsOwnActiveTemplate_Recorded_AndAnEarlierScreenKeepsIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var reminder = new MessageTemplate(tenant.Id, "Saldo vencido", MessagePurpose.CreditReminder, "Debes {vencido}.");
        var retired = new MessageTemplate(tenant.Id, "Vieja", MessagePurpose.CreditDueSoon, "Vence {porvencer}.");
        retired.SetActive(false);
        var notice = new MessageTemplate(tenant.Id, "Por vencer", MessagePurpose.CreditDueSoon,
            "Hola {nombre}, el {fecha} vence {porvencer}.", "por_vencer");
        fixture.Context.MessageTemplates.AddRange(reminder, retired, notice);
        await fixture.Context.SaveChangesAsync();
        var handler = UpdateRemindersHandler(fixture);
        UpdateCreditRemindersCommand Notice(bool enabled, int daysBefore = 3, Guid? templateId = null)
            => new() { Enabled = false, DueSoon = new UpdateCreditDueSoon { Enabled = enabled, DaysBefore = daysBefore, TemplateId = templateId } };

        var withoutWhatsApp = await handler.Handle(Notice(true), default);
        await ConnectWhatsAppAsync(fixture, tenant);
        var reminderTemplate = await handler.Handle(Notice(true, templateId: reminder.Id), default);
        var retiredTemplate = await handler.Handle(Notice(true, templateId: retired.Id), default);
        var on = await handler.Handle(Notice(true, templateId: notice.Id), default);
        // A screen from before the notice sends only the reminders: the notice stays as it is.
        var earlierScreen = await handler.Handle(new UpdateCreditRemindersCommand { Enabled = false }, default);

        Assert.Equal(MessagingDefaults.WhatsAppNotConfigured, withoutWhatsApp.Error);
        Assert.Equal(UpdateCreditRemindersCommandHandler.DueSoonTemplateNotFoundMessage, reminderTemplate.Error);
        Assert.Equal(UpdateCreditRemindersCommandHandler.DueSoonTemplateNotFoundMessage, retiredTemplate.Error);
        Assert.True(on.IsSuccess, on.Error);
        var dueSoon = on.Value.DueSoon;
        Assert.Equal((true, 3, (Guid?)notice.Id, false), (dueSoon.Enabled, dueSoon.DaysBefore, dueSoon.TemplateId, on.Value.Enabled));
        Assert.Equal("Por vencer", Assert.Single(dueSoon.Templates).Name);
        Assert.Equal("Saldo vencido", Assert.Single(on.Value.Templates).Name);
        Assert.Equal(["nombre", "negocio", "monto", "porvencer", "fecha"], dueSoon.Placeholders);
        Assert.Equal((MessagingDefaults.CreditDueSoonBody, 1, 7), (dueSoon.DefaultBody, dueSoon.MinDaysBefore, dueSoon.MaxDaysBefore));
        Assert.True(earlierScreen.IsSuccess, earlierScreen.Error);
        Assert.Equal((true, 3, (Guid?)notice.Id), (earlierScreen.Value.DueSoon.Enabled, earlierScreen.Value.DueSoon.DaysBefore, earlierScreen.Value.DueSoon.TemplateId));
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CreditRemindersUpdated").ToListAsync());
        Assert.Contains("\"DueSoonEnabled\":true", log.Metadata);
        Assert.Contains("\"DueSoonDaysBefore\":3", log.Metadata);

        // Por cobrar says it; turning it off needs no WhatsApp.
        Assert.Equal(3, (await ReceivablesAsync(fixture, new GetReceivablesQuery())).CreditDueSoonDaysBefore);
        fixture.Context.WhatsAppConfigs.RemoveRange(fixture.Context.WhatsAppConfigs);
        await fixture.Context.SaveChangesAsync();
        var off = await handler.Handle(Notice(false, templateId: notice.Id), default);
        Assert.True(off.IsSuccess, off.Error);
        Assert.False(off.Value.DueSoon.Enabled);
        Assert.Null((await ReceivablesAsync(fixture, new GetReceivablesQuery())).CreditDueSoonDaysBefore);

        // A template retired after it was chosen reads as the default text, which the notice sends instead.
        notice.SetActive(false);
        await fixture.Context.SaveChangesAsync();
        var retiredChoice = await new GetCreditRemindersQueryHandler(fixture.Context, fixture.TenantService).Handle(new GetCreditRemindersQuery(), default);
        Assert.Null(retiredChoice.DueSoon.TemplateId);
        Assert.Empty(retiredChoice.DueSoon.Templates);

        var validator = new UpdateCreditRemindersCommandValidator();
        Assert.Equal(TenantSettings.CreditDueSoonDaysBeforeMessage, Assert.Single(validator.Validate(Notice(true, daysBefore: 8)).Errors).ErrorMessage);
        Assert.False(validator.Validate(Notice(true, daysBefore: 0)).IsValid);
        Assert.True(validator.Validate(new UpdateCreditRemindersCommand()).IsValid);
        Assert.True(typeof(UpdateCreditRemindersCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
    }

    [Fact]
    public async Task Account_ShouldSay_WhenTheLastNoticeWentOut()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        await SetBusinessTermAsync(fixture, 7);
        await TurnDueSoonOnAsync(fixture, tenant, daysBefore: 2);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SoldOn(9, 28));
        Assert.Null((await AccountAsync(fixture, ana.Id)).LastDueSoonAt);

        Assert.Equal(1, await QueueAsync(fixture));
        var notice = await fixture.Context.OutgoingMessages.SingleAsync();
        // Queued is not sent.
        Assert.Null((await AccountAsync(fixture, ana.Id)).LastDueSoonAt);
        notice.MarkSent(SalesTestFixture.Now.AddMinutes(1), "wamid.1");
        await fixture.Context.SaveChangesAsync();

        var account = await AccountAsync(fixture, ana.Id);
        Assert.Equal((SalesTestFixture.Now.AddMinutes(1), (DateTimeOffset?)null), (account.LastDueSoonAt!.Value, account.LastReminderAt));
    }

    private static async Task TurnDueSoonOnAsync(SalesTestFixture fixture, Tenant tenant, int daysBefore, Guid? templateId = null)
    {
        await ConnectWhatsAppAsync(fixture, tenant);
        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditDueSoon(true, daysBefore, templateId);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task ForgiveAsync(SalesTestFixture fixture, Guid orderId)
    {
        var order = await fixture.Context.Orders.Include(o => o.WriteOffs).SingleAsync(o => o.Id == orderId);
        order.WriteOff(order.Balance, OrderWriteOffReason.Courtesy, null, fixture.Clock.UtcNow, "dueno");
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>One pass of the planner, as the worker runs it, saved.</summary>
    private static async Task<int> QueueAsync(SalesTestFixture fixture, int passLimit = CreditDueSoonPlanner.MaxPerPass)
    {
        var settings = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync();
        var queued = await new CreditDueSoonPlanner(fixture.Context, new PhoneNormalizationService(), fixture.Clock) { PassLimit = passLimit }
            .QueueDueAsync(settings, default);
        await SaveQueuedAsync(fixture);
        return queued;
    }
}
