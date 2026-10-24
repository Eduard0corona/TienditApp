using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Condonar y plazo del fiado (Fase 26B, ADR 0034): the owner forgives part or all of what a customer owes from their
/// account, each part a record apart on its order that lowers the balance, with no money moving; and the term of the fiado
/// tells what is overdue in Por cobrar and the account.
/// </summary>
public class CustomerWriteOffFlowTests
{
    private const string AnaPhone = "+525512345678";

    [Fact]
    public async Task WriteOff_ShouldGoFromTheOldestOrder_Recorded_WithoutMovingMoney()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var older = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SalesTestFixture.Now.AddDays(-3));
        var newer = await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now);

        var result = await WriteOffHandler(fixture).Handle(WriteOff(ana, 250m, expectedOwed: 300m) with
        {
            Reason = OrderWriteOffReason.WontBeCollected,
            Notes = "Se mudó"
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(50m, result.Value.Owed);
        Assert.Equal(
            [(older.OrderNumber, 200m, 0m), (newer.OrderNumber, 50m, 50m)],
            (await fixture.Context.Orders.OrderBy(o => o.CreatedAt).ToListAsync()).Select(o => (o.OrderNumber, o.WrittenOffAmount, o.Balance)));
        var line = Assert.Single(result.Value.Orders);
        Assert.Equal((newer.OrderNumber, 100m, 0m, 50m, 50m), (line.OrderNumber, line.Total, line.Paid, line.WrittenOff, line.Balance));
        Assert.Equal(
            [(newer.OrderNumber, 50m), (older.OrderNumber, 200m)],
            result.Value.RecentWriteOffs.OrderBy(w => w.Amount).Select(w => (w.OrderNumber, w.Amount)));
        Assert.All(result.Value.RecentWriteOffs, w => Assert.Equal((OrderWriteOffReason.WontBeCollected, "Se mudó"), (w.Reason, w.Notes)));

        var records = await fixture.Context.OrderWriteOffs.ToListAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal((SalesTestFixture.Now, fixture.CurrentUser.UserName), (r.WrittenOffAt, r.WrittenOffBy)));
        // No money moved: the drawer and the payments stay as they were, and the sales keep their total.
        Assert.Empty(await fixture.Context.OrderPayments.ToListAsync());
        Assert.Empty(await fixture.Context.CashSessions.Where(s => s.Id == session.Id).SelectMany(s => s.Transactions).ToListAsync());
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerAccountWrittenOff").ToListAsync());
        Assert.Equal((nameof(Customer), ana.Id.ToString()), (log.EntityName, log.EntityId));
        Assert.Contains("\"Reason\":\"WontBeCollected\"", log.Metadata);
        Assert.True(typeof(WriteOffCustomerAccountCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
    }

    [Fact]
    public async Task WriteOff_ShouldForgiveOneOrder_OnlyUpToWhatItOwes()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var older = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SalesTestFixture.Now.AddDays(-3));
        var newer = await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        var handler = WriteOffHandler(fixture);

        var above = await handler.Handle(WriteOff(ana, 150m, expectedOwed: 300m) with { OrderId = newer.Id }, default);
        var forgiven = await handler.Handle(WriteOff(ana, 100m, expectedOwed: 300m) with
        {
            OrderId = newer.Id,
            Reason = OrderWriteOffReason.RecordingError
        }, default);

        Assert.Equal(Order.WriteOffAboveBalanceMessage(100m), above.Error);
        Assert.True(forgiven.IsSuccess, forgiven.Error);
        Assert.Equal((200m, older.OrderNumber), (forgiven.Value.Owed, Assert.Single(forgiven.Value.Orders).OrderNumber));
        Assert.Equal(0m, (await fixture.Context.Orders.SingleAsync(o => o.Id == older.Id)).WrittenOffAmount);
    }

    [Fact]
    public async Task WriteOff_ShouldBeRefused_WhenTheAccountChanged_OrAboveWhatIsOwed_OrNotOneOfItsOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", "+525566667777", creditLimit: 1000m);
        await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        var luisSale = await SellOnCreditAsync(fixture, luis, soap, quantity: 1, createdAt: SalesTestFixture.Now);
        var handler = WriteOffHandler(fixture);

        var changed = await handler.Handle(WriteOff(ana, 50m, expectedOwed: 80m), default);
        var above = await handler.Handle(WriteOff(ana, 100.01m, expectedOwed: 100m), default);
        var otherCustomer = await handler.Handle(WriteOff(ana, 50m, expectedOwed: 100m) with { OrderId = luisSale.Id }, default);
        var nobody = await AddCustomerAsync(fixture, tenant, "Sofía Ramírez", "+525599990000");
        var nothing = await handler.Handle(WriteOff(nobody, 10m, expectedOwed: 10m), default);

        Assert.Equal(WriteOffCustomerAccountCommandHandler.OwedChangedMessage(100m), changed.Error);
        Assert.Equal(WriteOffCustomerAccountCommandHandler.AboveOwedMessage(100m), above.Error);
        Assert.Equal(WriteOffCustomerAccountCommandHandler.OrderNotOwedMessage, otherCustomer.Error);
        Assert.Equal(WriteOffCustomerAccountCommandHandler.NothingOwedMessage, nothing.Error);
        Assert.Empty(await fixture.Context.OrderWriteOffs.ToListAsync());
    }

    [Fact]
    public async Task Payment_ShouldTakeWhatIsLeft_AfterAWriteOff()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SalesTestFixture.Now);
        Assert.True((await WriteOffHandler(fixture).Handle(WriteOff(ana, 120m, expectedOwed: 200m), default)).IsSuccess);
        var pay = PayHandler(fixture);

        var above = await pay.Handle(new PayCustomerAccountCommand { CustomerId = ana.Id, Amount = 80.01m, Method = OrderPaymentMethod.Cash }, default);
        var paid = await pay.Handle(new PayCustomerAccountCommand { CustomerId = ana.Id, Amount = 80m, Method = OrderPaymentMethod.Cash, ExpectedOwed = 80m }, default);

        Assert.Equal(PayCustomerAccountCommandHandler.AboveOwedMessage(80m), above.Error);
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(0m, paid.Value.Owed);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Id);
        Assert.Equal((200m, 80m, 120m, 0m), (order.Total, order.PaidAmount, order.WrittenOffAmount, order.Balance));

        // The order says what was forgiven, by whom and why.
        var detail = await new GetOrderByIdQueryHandler(fixture.Context)
            .Handle(new GetOrderByIdQuery(sale.Id), default);
        Assert.Equal((120m, 0m), (detail.WrittenOffAmount, detail.Balance));
        var writeOff = Assert.Single(detail.WriteOffs);
        Assert.Equal((120m, OrderWriteOffReason.Courtesy, fixture.CurrentUser.UserName), (writeOff.Amount, writeOff.Reason, writeOff.WrittenOffBy));
    }

    [Fact]
    public void WriteOff_ShouldSayWhy_WhenTheReasonIsAnother_AndCarryWhatTheAccountOwed()
    {
        var validator = new WriteOffCustomerAccountCommandValidator();
        var command = new WriteOffCustomerAccountCommand
        {
            Phone = AnaPhone,
            Amount = 10m,
            Reason = OrderWriteOffReason.Courtesy,
            ExpectedOwed = 100m
        };

        Assert.True(validator.Validate(command).IsValid);
        Assert.Contains(OrderWriteOff.NotesNeededMessage,
            validator.Validate(command with { Reason = OrderWriteOffReason.Other, Notes = "  " }).Errors.Select(e => e.ErrorMessage));
        Assert.True(validator.Validate(command with { Reason = OrderWriteOffReason.Other, Notes = "Pagó con trabajo" }).IsValid);
        Assert.False(validator.Validate(command with { Amount = 10.005m }).IsValid);
        Assert.False(validator.Validate(command with { Amount = 0m }).IsValid);
        Assert.False(validator.Validate(command with { ExpectedOwed = 0m }).IsValid);
        Assert.False(validator.Validate(command with { Reason = (OrderWriteOffReason)7 }).IsValid);
        Assert.False(validator.Validate(command with { Phone = null }).IsValid);
        Assert.False(validator.Validate(command with { Notes = new string('a', OrderWriteOff.MaxNotesLength + 1) }).IsValid);
    }

    [Fact]
    public async Task CreditTerm_ShouldBeTheOwnersDecision_Recorded_FromOneTo120Days()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        var handler = new UpdateCustomerCreditTermCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var set = await handler.Handle(new UpdateCustomerCreditTermCommand { CreditTermDays = 15 }, default);
        var again = await handler.Handle(new UpdateCustomerCreditTermCommand { CreditTermDays = 15 }, default);
        var none = await handler.Handle(new UpdateCustomerCreditTermCommand { CreditTermDays = null }, default);

        Assert.Equal((15, 15, (int?)null), (set.Value.CreditTermDays, again.Value.CreditTermDays, none.Value.CreditTermDays));
        Assert.Null((await fixture.Context.TenantSettings.SingleAsync()).CreditTermDays);
        var logs = await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerCreditTermChanged").ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.True(typeof(UpdateCustomerCreditTermCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));

        var validator = new UpdateCustomerCreditTermCommandValidator();
        Assert.Equal(TenantSettings.CreditTermRangeMessage,
            Assert.Single(validator.Validate(new UpdateCustomerCreditTermCommand { CreditTermDays = 0 }).Errors).ErrorMessage);
        Assert.False(validator.Validate(new UpdateCustomerCreditTermCommand { CreditTermDays = 121 }).IsValid);
        Assert.True(validator.Validate(new UpdateCustomerCreditTermCommand { CreditTermDays = 120 }).IsValid);
    }

    [Fact]
    public async Task Overdue_ShouldBeWhatIsPastTheTerm_InPorCobrar_AndTheAccount()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var luis = await AddCustomerAsync(fixture, tenant, "Luis Pérez", "+525566667777", creditLimit: 1000m);
        // Now is Sunday 4 Oct, 11:00 in Mexico City. With 7 days, what was sold on 26 Sep is due on 3 Oct: overdue today.
        var overdue = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.FromHours(-6)));
        // Sold on 27 Sep at 23:30, its last day to pay is today.
        var dueToday = await SellOnCreditAsync(fixture, ana, soap, quantity: 1, createdAt: new DateTimeOffset(2026, 9, 27, 23, 30, 0, TimeSpan.FromHours(-6)));
        await SellOnCreditAsync(fixture, luis, soap, quantity: 1, createdAt: SalesTestFixture.Now.AddDays(-1));
        var receivables = new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock);

        var withoutTerm = await receivables.Handle(new GetReceivablesQuery(), default);
        Assert.Equal((0m, (int?)null), (withoutTerm.TotalOverdue, withoutTerm.CreditTermDays));
        Assert.Empty((await receivables.Handle(new GetReceivablesQuery { OverdueOnly = true }, default)).Page.Items);

        (await fixture.Context.TenantSettings.SingleAsync()).SetCreditTermDays(7);
        await fixture.Context.SaveChangesAsync();
        var all = await receivables.Handle(new GetReceivablesQuery(), default);
        var onlyOverdue = await receivables.Handle(new GetReceivablesQuery { OverdueOnly = true }, default);

        Assert.Equal((400m, 200m, (int?)7), (all.TotalOwed, all.TotalOverdue, all.CreditTermDays));
        Assert.Equal([(AnaPhone, 300m, 200m), ("+525566667777", 100m, 0m)], all.Page.Items.Select(r => (r.Phone, r.Owed, r.Overdue)));
        // Whoever has something overdue, with all they owe.
        var row = Assert.Single(onlyOverdue.Page.Items);
        Assert.Equal((AnaPhone, 300m, 200m), (row.Phone, row.Owed, row.Overdue));
        Assert.Equal((300m, 200m), (onlyOverdue.TotalOwed, onlyOverdue.TotalOverdue));

        var account = await AccountAsync(fixture, ana.Id);
        Assert.Equal((7, 200m), (account.CreditTermDays, account.Overdue));
        Assert.Equal(
            [(overdue.OrderNumber, new DateOnly(2026, 10, 3), true), (dueToday.OrderNumber, new DateOnly(2026, 10, 4), false)],
            account.Orders.Select(o => (o.OrderNumber, o.DueOn!.Value, o.IsOverdue)));

        // Tomorrow, the one due today is overdue too.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(1);
        Assert.Equal(300m, (await AccountAsync(fixture, ana.Id)).Overdue);
    }

    [Fact]
    public void CreditTerm_ShouldCountBusinessDays_InTheBusinessTimeZone()
    {
        var term = new CreditTerm.Settings(1, "America/Mexico_City");
        // 23:30 on 1 Oct in Mexico City is already 2 Oct in UTC: its last day to pay is 2 Oct, not 3 Oct.
        var lateSale = new DateTimeOffset(2026, 10, 2, 5, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 10, 2), term.DueOn(lateSale));
        // On 3 Oct at 00:30 local (06:30 UTC) it is overdue; on 2 Oct at 23:59 local it is not yet.
        Assert.True(lateSale < term.OverdueBefore(new DateTimeOffset(2026, 10, 3, 6, 30, 0, TimeSpan.Zero)));
        Assert.False(lateSale < term.OverdueBefore(new DateTimeOffset(2026, 10, 3, 5, 59, 0, TimeSpan.Zero)));
        Assert.Equal(DateTimeOffset.MinValue, new CreditTerm.Settings(null, "America/Mexico_City").OverdueBefore(lateSale));
        Assert.Null(new CreditTerm.Settings(null, null).DueOn(lateSale));
    }

    [Fact]
    public async Task Overdue_ShouldCountFromWhenItWasHandedOver_NotFromWhenItWasMade()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        (await fixture.Context.TenantSettings.SingleAsync()).SetCreditTermDays(7);
        await fixture.Context.SaveChangesAsync();
        // Now is 4 Oct. Both were received on 14 Sep (a service picked up later); one was handed over unpaid on 3 Oct, the
        // other on 20 Sep. Its term starts the day it was handed over: only the second is overdue.
        var made = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.FromHours(-6));
        var pickedUpYesterday = await HandOverUnpaidAsync(fixture, ana, soap, made, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(-6)));
        var pickedUpLongAgo = await HandOverUnpaidAsync(fixture, ana, soap, made, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(-6)));

        var account = await AccountAsync(fixture, ana.Id);
        var receivables = await new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetReceivablesQuery(), default);

        Assert.Equal(100m, account.Overdue);
        Assert.Equal(
            [(pickedUpYesterday.Id, new DateOnly(2026, 10, 10), false), (pickedUpLongAgo.Id, new DateOnly(2026, 9, 27), true)],
            account.Orders.OrderBy(o => o.DueOn).Reverse().Select(o => (o.OrderId, o.DueOn!.Value, o.IsOverdue)));
        Assert.Equal((200m, 100m), (receivables.TotalOwed, receivables.TotalOverdue));
    }

    [Fact]
    public void CreditTerm_ShouldStartADayWithoutMidnight_AtItsFirstMinute()
    {
        // Clocks that jump from 00:00 to 01:00 on 6 Sep, as in Santiago or Havana: that day has no 00:00.
        var jump = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 9, 6);
        var back = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 4, 5);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), jump, back);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Midnight jump", TimeSpan.FromHours(-4), "Midnight jump", "Standard", "Daylight", [rule]);

        var start = TenantDateTimeResolver.StartOfLocalDayUtc(new DateTime(2026, 9, 6), zone);

        // 01:00 at UTC-3 (daylight), instead of throwing inside the payment or write-off that reads the account.
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 3, 0, 0, TimeSpan.Zero), TenantDateTimeResolver.StartOfLocalDayUtc(new DateTime(2026, 9, 7), zone));
    }

    [Fact]
    public async Task Reverse_ShouldOweItAgain_KeepingTheRecord_Once()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 3, createdAt: SalesTestFixture.Now);
        Assert.True((await WriteOffHandler(fixture).Handle(WriteOff(ana, 120m, expectedOwed: 300m), default)).IsSuccess);
        var writeOff = await fixture.Context.OrderWriteOffs.SingleAsync();
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(2);

        var result = await ReverseHandler(fixture).Handle(new ReverseCustomerWriteOffCommand { WriteOffId = writeOff.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(300m, result.Value.Owed);
        var line = Assert.Single(result.Value.Orders);
        Assert.Equal((sale.OrderNumber, 0m, 300m), (line.OrderNumber, line.WrittenOff, line.Balance));
        // The record stays, with when and who undid it.
        var shown = Assert.Single(result.Value.RecentWriteOffs);
        Assert.Equal((writeOff.Id, 120m, SalesTestFixture.Now.AddHours(2)), (shown.Id, shown.Amount, shown.ReversedAt!.Value));
        var record = await fixture.Context.OrderWriteOffs.SingleAsync();
        Assert.Equal((SalesTestFixture.Now.AddHours(2), fixture.CurrentUser.UserName), (record.ReversedAt!.Value, record.ReversedBy));
        Assert.Equal(0m, (await fixture.Context.Orders.SingleAsync()).WrittenOffAmount);
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerAccountWriteOffReversed").ToListAsync());
        Assert.Equal((nameof(Customer), ana.Id.ToString()), (log.EntityName, log.EntityId));

        // Sent again, it undoes nothing more; one that does not exist here is not found.
        var again = await ReverseHandler(fixture).Handle(new ReverseCustomerWriteOffCommand { WriteOffId = writeOff.Id }, default);
        Assert.Equal(OrderWriteOff.AlreadyReversedMessage, again.Error);
        Assert.Equal(300m, (await AccountAsync(fixture, ana.Id)).Owed);
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            ReverseHandler(fixture).Handle(new ReverseCustomerWriteOffCommand { WriteOffId = Guid.NewGuid() }, default));
        Assert.True(typeof(ReverseCustomerWriteOffCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
    }

    [Fact]
    public async Task OrderAnswers_ShouldKeepWhatWasForgiven_AfterACharge()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SalesTestFixture.Now);
        Assert.True((await WriteOffHandler(fixture).Handle(WriteOff(ana, 50m, expectedOwed: 200m), default)).IsSuccess);

        // Pedidos swaps in the order the charge answers: what was forgiven stays in it.
        var charged = await fixture.AddPaymentHandler().Handle(new AddOrderPaymentCommand
        {
            OrderId = sale.Id,
            Amount = 50m,
            Method = OrderPaymentMethod.Cash
        }, default);

        Assert.True(charged.IsSuccess, charged.Error);
        Assert.Equal((50m, 100m), (charged.Value.WrittenOffAmount, charged.Value.Balance));
        Assert.Equal(50m, Assert.Single(charged.Value.WriteOffs).Amount);
    }

    [Fact]
    public async Task WhatWasForgiven_ShouldNotBeOwed_InTheCardAndHome_ButTheInvoiceGoesForTheWholeSale()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await SellOnCreditAsync(fixture, ana, soap, quantity: 2, createdAt: SalesTestFixture.Now);
        Assert.True((await PayHandler(fixture).Handle(new PayCustomerAccountCommand
        {
            CustomerId = ana.Id,
            Amount = 50m,
            Method = OrderPaymentMethod.Cash,
            CashReceived = 50m
        }, default)).IsSuccess);
        Assert.True((await WriteOffHandler(fixture).Handle(WriteOff(ana, 100m, expectedOwed: 150m), default)).IsSuccess);

        var card = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { CustomerId = ana.Id }, default);
        var home = await new GetDashboardMetricsQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDashboardMetricsQuery(), default);
        var invoice = await new GetOrderCfdiDraftQueryHandler(fixture.Context).Handle(new GetOrderCfdiDraftQuery { OrderId = sale.Id }, default);

        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal((50m, 50m), (card.Value.Balance, Assert.Single(card.Value.RecentOrders).Balance));
        Assert.Equal((50m, 1), (home.OutstandingBalance, home.UnpaidOrders));
        // The invoice goes for the $200 sale: the $150 not paid stays unpaid (PPD), and it says the $100 needs a credit note.
        Assert.True(invoice.IsSuccess, invoice.Error);
        Assert.Equal("PPD", invoice.Value.PaymentMethod);
        Assert.Contains(GetOrderCfdiDraftQueryHandler.WrittenOffWarning(100m), invoice.Value.Warnings);
    }

    private static async Task<(Tenant Tenant, Product Soap)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        return (tenant, soap);
    }

    private static async Task<Customer> AddCustomerAsync(SalesTestFixture fixture, Tenant tenant, string name, string phone, decimal? creditLimit = null)
    {
        var customer = new Customer(tenant.Id, name, phone, null);
        if (creditLimit is not null)
            customer.SetCreditLimit(creditLimit);
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

    /// <summary>A sale of soap left unpaid, made on <paramref name="createdAt"/> and handed over on <paramref name="handedOverAt"/>.</summary>
    private static async Task<Order> HandOverUnpaidAsync(
        SalesTestFixture fixture, Customer customer, Product soap, DateTimeOffset createdAt, DateTimeOffset handedOverAt)
    {
        var sale = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
        {
            CustomerId = customer.Id,
            Items = [new CreateOrderItemDto { ProductId = soap.Id, Quantity = 1 }]
        }, default);
        Assert.True(sale.IsSuccess, sale.Error);
        var order = await fixture.Context.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.Id == sale.Value.Id);
        order.CreatedAt = createdAt;
        order.TransitionTo(OrderStatus.Delivered, OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail), "test");
        await fixture.Context.SaveChangesAsync();
        // The history stamps the real clock: the test says when it was handed over.
        var delivered = order.StatusHistory.Single(h => h.NewStatus == OrderStatus.Delivered);
        fixture.Context.Entry(delivered).Property(h => h.ChangedAt).CurrentValue = handedOverAt;
        await fixture.Context.SaveChangesAsync();
        return order;
    }

    private static WriteOffCustomerAccountCommand WriteOff(Customer customer, decimal amount, decimal expectedOwed) => new()
    {
        CustomerId = customer.Id,
        Amount = amount,
        Reason = OrderWriteOffReason.Courtesy,
        ExpectedOwed = expectedOwed
    };

    private static WriteOffCustomerAccountCommandHandler WriteOffHandler(SalesTestFixture fixture) => new(
        fixture.Context,
        fixture.TenantService,
        fixture.CurrentUser,
        new PhoneNormalizationService(),
        fixture.Clock,
        new CustomerCreditLock(fixture.Context));

    private static ReverseCustomerWriteOffCommandHandler ReverseHandler(SalesTestFixture fixture) => new(
        fixture.Context,
        fixture.TenantService,
        fixture.CurrentUser,
        new PhoneNormalizationService(),
        fixture.Clock,
        new CustomerCreditLock(fixture.Context),
        new InvoicingLock(fixture.Context));

    private static PayCustomerAccountCommandHandler PayHandler(SalesTestFixture fixture) => new(
        fixture.Context,
        fixture.TenantService,
        fixture.CurrentUser,
        new PhoneNormalizationService(),
        fixture.Clock,
        new OrderCashService(fixture.Context, fixture.CurrentUser),
        new CustomerCreditLock(fixture.Context));

    private static async Task<CustomerAccountDto> AccountAsync(SalesTestFixture fixture, Guid customerId)
    {
        var result = await new GetCustomerAccountQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetCustomerAccountQuery { CustomerId = customerId }, default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }
}
