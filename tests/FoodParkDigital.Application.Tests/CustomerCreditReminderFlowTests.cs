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
using MediatR;
using Microsoft.EntityFrameworkCore;

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
        // Turning them off needs no WhatsApp: a business that disconnected it can stop them.
        fixture.Context.WhatsAppConfigs.RemoveRange(fixture.Context.WhatsAppConfigs);
        await fixture.Context.SaveChangesAsync();
        Assert.False((await handler.Handle(new UpdateCreditRemindersCommand { Enabled = false }, default)).Value.Enabled);
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

    private static async Task<(Tenant Tenant, Product Soap)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        return (tenant, soap);
    }

    private static async Task<Customer> AddCustomerAsync(
        SalesTestFixture fixture, Tenant tenant, string name, string phone, decimal? creditLimit = null, int? termDays = null)
    {
        var customer = new Customer(tenant.Id, name, phone, null);
        if (creditLimit is not null)
            customer.SetCreditLimit(creditLimit);
        customer.SetCreditTermDays(termDays);
        fixture.Context.Customers.Add(customer);
        await fixture.Context.SaveChangesAsync();
        return customer;
    }

    /// <summary>A sale of soap on credit, stamped when it was made (the in-memory context leaves the date to the test).</summary>
    private static async Task<Order> SellOnCreditAsync(SalesTestFixture fixture, Customer customer, Product soap, decimal quantity, DateTimeOffset createdAt)
    {
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            OnCredit = true,
            Items = [new CreateOrderItemDto { ProductId = soap.Id, Quantity = quantity }]
        }, default);
        Assert.True(sale.IsSuccess, sale.Error);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Value.Id);
        order.CreatedAt = createdAt;
        await fixture.Context.SaveChangesAsync();
        return order;
    }

    private static async Task SetBusinessTermAsync(SalesTestFixture fixture, int? days)
    {
        (await fixture.Context.TenantSettings.SingleAsync()).SetCreditTermDays(days);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task ConnectWhatsAppAsync(SalesTestFixture fixture, Tenant tenant)
    {
        var config = new WhatsAppConfig(tenant.Id);
        config.Update(true, "token", "123456", "waba", "+52 55 0000 0000");
        fixture.Context.WhatsAppConfigs.Add(config);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task TurnRemindersOnAsync(SalesTestFixture fixture, Tenant tenant, int everyDays, Guid? templateId = null)
    {
        await ConnectWhatsAppAsync(fixture, tenant);
        (await fixture.Context.TenantSettings.SingleAsync()).UpdateCreditReminders(true, everyDays, templateId);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>One pass of the planner, as the worker runs it, saved.</summary>
    private static async Task<int> QueueAsync(SalesTestFixture fixture)
    {
        var settings = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync();
        var queued = await new CreditReminderPlanner(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .QueueDueAsync(settings, default);
        // The in-memory context leaves the date to the test: stamp it as the database would.
        foreach (var entry in fixture.Context.ChangeTracker.Entries<OutgoingMessage>().Where(e => e.State == EntityState.Added))
            entry.Entity.CreatedAt = fixture.Clock.UtcNow;
        await fixture.Context.SaveChangesAsync();
        return queued;
    }

    private static UpdateCreditRemindersCommandHandler UpdateRemindersHandler(SalesTestFixture fixture)
    {
        var context = fixture.Context;
        var mediator = new GetRemindersMediator(new GetCreditRemindersQueryHandler(context, fixture.TenantService));
        return new UpdateCreditRemindersCommandHandler(context, fixture.TenantService, fixture.CurrentUser, mediator);
    }

    private static async Task<ReceivablesDto> ReceivablesAsync(SalesTestFixture fixture, GetReceivablesQuery query)
        => await new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock).Handle(query, default);

    private static async Task<CustomerAccountDto> AccountAsync(SalesTestFixture fixture, Guid customerId)
    {
        var result = await new GetCustomerAccountQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetCustomerAccountQuery { CustomerId = customerId }, default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    /// <summary>Answers the settings query the update command sends back, and nothing else.</summary>
    private sealed class GetRemindersMediator(GetCreditRemindersQueryHandler handler) : IMediator
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => request is GetCreditRemindersQuery query ? (TResponse)(object)await handler.Handle(query, ct) : default!;
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public List<(string Kind, string Phone)> Sent { get; } = [];

        public Task<MessageSendResult> SendTemplateMessageAsync(Guid tenantId, string phoneNumber, string templateName, string languageCode, string[] parameters, CancellationToken cancellationToken = default)
        {
            Sent.Add(($"template:{templateName}", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }

        public Task<MessageSendResult> SendTextMessageAsync(Guid tenantId, string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(("text", phoneNumber));
            return Task.FromResult(MessageSendResult.Sent($"wamid.{Sent.Count}"));
        }
    }
}
