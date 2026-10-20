using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Saldo a favor del cliente (Fase 26D, ADR 0036): what a customer leaves enters the drawer of whoever charges as a movement
/// of its own and adds to their balance; paying with it, in Vender or from their account, records the sale outside every
/// drawer; a refund can go to it; what is left of a payment can stay in it; it is given back out of a drawer; and the cut,
/// the CFDI, the account, Por cobrar and the card show it.
/// </summary>
public class CustomerBalanceFlowTests
{
    private const string AnaPhone = "+525512345678";

    [Fact]
    public async Task Deposit_ShouldEnterTheCashiersDrawer_AsAMovementOfItsOwn_AndAddToTheBalance()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var session = await fixture.OpenCashSessionAsync(tenant);

        var cash = await DepositHandler(fixture).Handle(Deposit(ana, 200m, OrderPaymentMethod.Cash, expected: 0m), default);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);
        var card = await DepositHandler(fixture).Handle(
            Deposit(ana, 100m, OrderPaymentMethod.Card, expected: 200m) with { Reference = "Voucher 77", Notes = "Para el pastel" }, default);

        Assert.True(cash.IsSuccess, cash.Error);
        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal(300m, card.Value.CreditBalance);
        Assert.Equal([100m, 200m], card.Value.RecentBalanceEntries.Select(e => e.Amount));
        Assert.Equal((CustomerBalanceEntryKind.Deposit, (OrderPaymentMethod?)OrderPaymentMethod.Card, "Voucher 77", "Para el pastel"),
            (card.Value.RecentBalanceEntries[0].Kind, card.Value.RecentBalanceEntries[0].Method,
                card.Value.RecentBalanceEntries[0].Reference, card.Value.RecentBalanceEntries[0].Notes));

        // Not a sale: no payment, a movement of its own in the drawer, in cash in the count and by card apart.
        Assert.Empty(await fixture.Context.OrderPayments.ToListAsync());
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.All(drawer.Transactions, t => Assert.Equal(CashTransactionType.CustomerDeposit, t.TransactionType));
        Assert.Equal((700m, 100m), (drawer.ExpectedBalance, drawer.NonCashInflows));
        Assert.Equal("Saldo a favor de Ana López", drawer.Transactions.Single(t => t.PaymentMethod == OrderPaymentMethod.Cash).Notes);
        Assert.All(await fixture.Context.CustomerBalanceEntries.ToListAsync(), e => Assert.Equal((session.Id, ana.Id, AnaPhone),
            (e.CashSessionId!.Value, e.CustomerId!.Value, e.Phone)));
        var logs = await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerBalanceDeposited").ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(ana.Id.ToString(), log.EntityId));
    }

    [Fact]
    public async Task Deposit_ShouldBeRefused_TwiceForTheSameScreen_WithoutADrawer_OrNotInPerson()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var deposit = Deposit(ana, 50m, OrderPaymentMethod.Cash, expected: 0m);

        var first = await DepositHandler(fixture).Handle(deposit, default);
        var again = await DepositHandler(fixture).Handle(deposit, default);
        session.Close(SalesTestFixture.Now, "cajero", session.ExpectedBalance);
        await fixture.Context.SaveChangesAsync();
        var noDrawer = await DepositHandler(fixture).Handle(deposit with { ExpectedBalance = 50m }, default);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(CustomerBalances.ChangedMessage(50m), again.Error);
        Assert.Equal(CustomerBalances.NoOpenDrawerMessage, noDrawer.Error);
        Assert.Single(await fixture.Context.CustomerBalanceEntries.ToListAsync());

        var validator = new AddCustomerDepositCommandValidator();
        foreach (var method in new[] { OrderPaymentMethod.CustomerBalance, OrderPaymentMethod.Platform, OrderPaymentMethod.Online, (OrderPaymentMethod)42 })
            Assert.Contains(CustomerBalanceEntry.InPersonMethodMessage, validator.Validate(deposit with { Method = method }).Errors.Select(e => e.ErrorMessage));
        Assert.False(validator.Validate(deposit with { Amount = 10.005m }).IsValid);
        Assert.False(validator.Validate(deposit with { Amount = 1_000_000.01m }).IsValid);
        Assert.False(validator.Validate(deposit with { CustomerId = null, Phone = null }).IsValid);
        Assert.True(typeof(AddCustomerDepositCommand).GetCustomAttributes(typeof(RequiresPermissionAttribute), false).Length == 1);
    }

    [Fact]
    public async Task Deposit_ShouldBeOfTheAccount_ByPhone_WithoutACustomerRecord()
    {
        await using var fixture = SalesTestFixture.Create();
        await ShopAsync(fixture);
        await fixture.OpenCashSessionAsync(await fixture.Context.Tenants.SingleAsync());

        var left = await DepositHandler(fixture).Handle(new AddCustomerDepositCommand
        {
            Phone = "55 1234 5678",
            Amount = 80m,
            Method = OrderPaymentMethod.Transfer
        }, default);

        Assert.True(left.IsSuccess, left.Error);
        Assert.Equal((AnaPhone, 80m, (Guid?)null), (left.Value.Phone, left.Value.CreditBalance, left.Value.CustomerId));
        var entry = await fixture.Context.CustomerBalanceEntries.SingleAsync();
        var log = await fixture.Context.AuditLogs.SingleAsync(l => l.Action == "CustomerBalanceDeposited");
        Assert.Equal((nameof(CustomerBalanceEntry), entry.Id.ToString()), (log.EntityName, log.EntityId));
        Assert.Equal("Saldo a favor", (await fixture.Context.CashTransactions.SingleAsync()).Notes);
    }

    [Fact]
    public async Task PayingWithTheBalance_InVender_ShouldRecordTheSale_OutsideEveryDrawer()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var session = await fixture.OpenCashSessionAsync(tenant);
        await DepositHandler(fixture).Handle(Deposit(ana, 150m, OrderPaymentMethod.Cash), default);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);

        var sale = await fixture.CreateOrderHandler().Handle(BalanceSale(ana, soap, fromBalance: 100m), default);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal((0m, OrderStatus.Delivered), (sale.Value.Balance, sale.Value.Status));
        var payment = await fixture.Context.OrderPayments.SingleAsync();
        Assert.Equal((OrderPaymentMethod.CustomerBalance, 100m), (payment.Method, payment.Amount));
        Assert.NotNull(payment.InventoryLocationId);
        var applied = await fixture.Context.CustomerBalanceEntries.SingleAsync(e => e.Kind == CustomerBalanceEntryKind.Applied);
        Assert.Equal((-100m, (Guid?)sale.Value.Id, (Guid?)payment.Id, (Guid?)ana.Id), (applied.Amount, applied.OrderId, applied.OrderPaymentId, applied.CustomerId));
        // It entered the drawer when she left it: nothing more enters now.
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.DoesNotContain(drawer.Transactions, t => t.TransactionType == CashTransactionType.SalePayment);
        Assert.Equal(650m, drawer.ExpectedBalance);
        var account = await AccountAsync(fixture, ana.Id);
        Assert.Equal(50m, account.CreditBalance);
        Assert.Equal(sale.Value.OrderNumber, account.RecentBalanceEntries[0].OrderNumber);

        // More than she has left is refused, whole.
        var above = await fixture.CreateOrderHandler().Handle(BalanceSale(ana, soap, fromBalance: 60m), default);
        Assert.Equal(string.Format(CustomerBalanceEntry.NotEnoughMessage, "$50.00"), above.Error);
        Assert.Equal(1, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task PayingWithTheBalance_InVender_ShouldNeedHerPhone_Connection_AndNoTip()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var validator = new CreateOrderCommandValidator();
        var sale = BalanceSale(ana, soap, fromBalance: 10m);

        Assert.True(validator.Validate(sale).IsValid);
        Assert.Contains(OrderPaymentMethodRules.CustomerBalanceNeedsPhoneMessage,
            Errors(validator, sale with { CustomerId = null, CustomerName = "Mostrador" }));
        Assert.Contains(CreateOrderCommandValidator.CustomerBalanceWithConnectionMessage,
            Errors(validator, sale with { IdempotencyKey = Guid.NewGuid(), CapturedOfflineAt = SalesTestFixture.Now }));
        Assert.Contains(OrderPaymentMethodRules.CustomerBalanceNoTipMessage,
            Errors(validator, sale with { Payments = [new CreateOrderPaymentDto { Amount = 10m, Method = OrderPaymentMethod.CustomerBalance, Tip = 5m }] }));
        Assert.Contains(CreateOrderCommandValidator.CustomerBalanceSaleMessage,
            Errors(validator, sale with { CreatedByBusiness = false }));

        // Nothing in her favor: nothing to pay with.
        await fixture.OpenCashSessionAsync(tenant);
        var refused = await fixture.CreateOrderHandler().Handle(sale, default);
        Assert.Equal(CustomerBalances.NothingInFavorMessage, refused.Error);
    }

    [Fact]
    public async Task PayingTheAccount_WithTheBalance_ShouldSettleTheOldestOrder_OutsideEveryDrawer()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var credit = await fixture.CreateOrderHandler().Handle(CreditSale(ana, soap, quantity: 2), default);
        Assert.True(credit.IsSuccess, credit.Error);
        await DepositHandler(fixture).Handle(Deposit(ana, 120m, OrderPaymentMethod.Cash), default);
        var payWithBalance = new PayCustomerAccountCommand
        {
            CustomerId = ana.Id,
            Amount = 120m,
            Method = OrderPaymentMethod.CustomerBalance,
            ExpectedOwed = 200m,
            ExpectedBalance = 120m
        };

        var above = await PayHandler(fixture).Handle(payWithBalance with { Amount = 150m }, default);
        var changed = await PayHandler(fixture).Handle(payWithBalance with { ExpectedBalance = 100m }, default);
        var paid = await PayHandler(fixture).Handle(payWithBalance, default);
        var again = await PayHandler(fixture).Handle(payWithBalance, default);
        var nothingLeft = await PayHandler(fixture).Handle(payWithBalance with { Amount = 50m, ExpectedOwed = 80m }, default);

        Assert.Equal(string.Format(CustomerBalanceEntry.NotEnoughMessage, "$120.00"), above.Error);
        Assert.Equal(CustomerBalances.ChangedMessage(120m), changed.Error);
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal((80m, 0m), (paid.Value.Owed, paid.Value.CreditBalance));
        Assert.Equal(PayCustomerAccountCommandHandler.OwedChangedMessage(80m), again.Error);
        Assert.Equal(CustomerBalances.ChangedMessage(0m), nothingLeft.Error);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.DoesNotContain(drawer.Transactions, t => t.TransactionType == CashTransactionType.SalePayment);
        var applied = await fixture.Context.CustomerBalanceEntries.SingleAsync(e => e.Kind == CustomerBalanceEntryKind.Applied);
        Assert.Equal(credit.Value.Id, applied.OrderId);

        var validator = new PayCustomerAccountCommandValidator();
        Assert.Contains(PayCustomerAccountCommandHandler.RestInFavorInPersonMessage,
            validator.Validate(payWithBalance with { RestInFavor = true }).Errors.Select(e => e.ErrorMessage));
        Assert.Contains(PayCustomerAccountCommandHandler.BalanceWithoutEvidenceMessage,
            validator.Validate(payWithBalance with { EvidenceUrl = "/api/v1/files/x" }).Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public async Task WhatIsPaidBeyondWhatSheOwes_ShouldStayInHerFavor_OnlyWhenAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var credit = await fixture.CreateOrderHandler().Handle(CreditSale(ana, soap, quantity: 1), default);
        Assert.True(credit.IsSuccess, credit.Error);
        var payment = new PayCustomerAccountCommand { CustomerId = ana.Id, Amount = 150m, Method = OrderPaymentMethod.Cash, CashReceived = 200m };

        var refused = await PayHandler(fixture).Handle(payment, default);
        var paid = await PayHandler(fixture).Handle(payment with { RestInFavor = true, ExpectedBalance = 0m }, default);

        Assert.Equal(PayCustomerAccountCommandHandler.AboveOwedMessage(100m), refused.Error);
        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal((0m, 50m), (paid.Value.Owed, paid.Value.CreditBalance));
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.Equal((100m, 50m), (
            drawer.Transactions.Where(t => t.TransactionType == CashTransactionType.SalePayment).Sum(t => t.Amount),
            drawer.Transactions.Where(t => t.TransactionType == CashTransactionType.CustomerDeposit).Sum(t => t.Amount)));
        // The change of what she handed over went with the payment: the parts add up to what the drawer received.
        Assert.Equal(150m, (await fixture.Context.OrderPayments.SingleAsync()).CashReceived);
        Assert.Equal(PayCustomerAccountCommandHandler.RestNote, (await fixture.Context.CustomerBalanceEntries.SingleAsync()).Notes);
        Assert.Contains("\"InFavor\":50", (await fixture.Context.AuditLogs.SingleAsync(l => l.Action == "CustomerAccountPaid")).Metadata);

        // Owing nothing, all of it stays in her favor when asked; otherwise there is nothing to pay.
        var nothing = await PayHandler(fixture).Handle(payment with { CashReceived = null }, default);
        var allInFavor = await PayHandler(fixture).Handle(payment with { CashReceived = null, RestInFavor = true, ExpectedBalance = 50m }, default);
        Assert.Equal(PayCustomerAccountCommandHandler.NothingOwedMessage, nothing.Error);
        Assert.True(allInFavor.IsSuccess, allInFavor.Error);
        Assert.Equal(200m, allInFavor.Value.CreditBalance);
    }

    [Fact]
    public async Task Refund_ToTheBalance_ShouldAddToTheAccount_WithoutLeavingADrawer_AndVoidingGivesBackWhatWasUsed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var paidInCash = await fixture.CreateOrderHandler().Handle(Sale(ana, soap) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash }]
        }, default);
        Assert.True(paidInCash.IsSuccess, paidInCash.Error);

        var refund = await RefundHandler(fixture).Handle(new RefundOrderCommand
        {
            OrderId = paidInCash.Value.Id,
            Amount = 30m,
            Method = OrderPaymentMethod.CustomerBalance,
            Reason = "No le gustó",
            RestockItems = false
        }, default);

        Assert.True(refund.IsSuccess, refund.Error);
        var entry = await fixture.Context.CustomerBalanceEntries.SingleAsync();
        Assert.Equal((CustomerBalanceEntryKind.Refund, 30m, (Guid?)ana.Id, (Guid?)paidInCash.Value.Id), (entry.Kind, entry.Amount, entry.CustomerId, entry.OrderId));
        Assert.Equal(entry.OrderRefundId, (await fixture.Context.OrderRefunds.SingleAsync()).Id);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.DoesNotContain(drawer.Transactions, t => t.TransactionType == CashTransactionType.Refund);
        Assert.Equal(600m, drawer.ExpectedBalance);

        // A sale paid with it, voided, gives it back to her balance.
        var withBalance = await fixture.CreateOrderHandler().Handle(BalanceSale(ana, soap, fromBalance: 30m), default);
        Assert.True(withBalance.IsSuccess, withBalance.Error);
        Assert.Equal(0m, (await AccountAsync(fixture, ana.Id)).CreditBalance);
        var voided = await VoidHandler(fixture).Handle(new VoidOrderCommand { OrderId = withBalance.Value.Id, Reason = "Se equivocó" }, default);
        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(30m, (await AccountAsync(fixture, ana.Id)).CreditBalance);
    }

    [Fact]
    public async Task Return_ShouldGiveBackUpToHerBalance_OutOfTheDrawer_ByWhoMayRefund()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var mario = await AddCustomerAsync(fixture, tenant, "Mario", "+525598765432");
        var session = await fixture.OpenCashSessionAsync(tenant);
        await DepositHandler(fixture).Handle(Deposit(ana, 100m, OrderPaymentMethod.Cash), default);
        var giveBack = new ReturnCustomerBalanceCommand { CustomerId = ana.Id, Amount = 40m, Method = OrderPaymentMethod.Cash, ExpectedBalance = 100m };

        var above = await ReturnHandler(fixture).Handle(giveBack with { Amount = 100.01m }, default);
        var changed = await ReturnHandler(fixture).Handle(giveBack with { ExpectedBalance = 90m }, default);
        var nothing = await ReturnHandler(fixture).Handle(giveBack with { CustomerId = mario.Id }, default);
        var returned = await ReturnHandler(fixture).Handle(giveBack, default);
        var again = await ReturnHandler(fixture).Handle(giveBack, default);

        Assert.Equal(string.Format(CustomerBalanceEntry.NotEnoughMessage, "$100.00"), above.Error);
        Assert.Equal(CustomerBalances.ChangedMessage(100m), changed.Error);
        Assert.Equal(CustomerBalances.NothingInFavorMessage, nothing.Error);
        Assert.True(returned.IsSuccess, returned.Error);
        Assert.Equal(60m, returned.Value.CreditBalance);
        Assert.Equal(CustomerBalances.ChangedMessage(60m), again.Error);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        var movement = drawer.Transactions.Single(t => t.TransactionType == CashTransactionType.CustomerDepositReturn);
        Assert.Equal((-40m, "Saldo a favor devuelto de Ana López"), (movement.Amount, movement.Notes));
        Assert.Equal(560m, drawer.ExpectedBalance);
        var entry = await fixture.Context.CustomerBalanceEntries.SingleAsync(e => e.Kind == CustomerBalanceEntryKind.Returned);
        Assert.Equal((-40m, (Guid?)session.Id, (OrderPaymentMethod?)OrderPaymentMethod.Cash), (entry.Amount, entry.CashSessionId, entry.Method));
        Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerBalanceReturned").ToListAsync());
        Assert.Equal([TenantPermission.ProcessRefunds], giveBack.RequiredPermissions());
    }

    [Fact]
    public async Task Cut_ShouldShowWhatWasLeftAndGivenBack_AndKeepWhatWasPaidWithTheBalance_OutOfTheMoney()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        await fixture.OpenCashSessionAsync(tenant);
        await DepositHandler(fixture).Handle(Deposit(ana, 200m, OrderPaymentMethod.Cash), default);
        await DepositHandler(fixture).Handle(Deposit(ana, 50m, OrderPaymentMethod.Card), default);
        var withBalance = await fixture.CreateOrderHandler().Handle(BalanceSale(ana, soap, fromBalance: 100m), default);
        var inCash = await fixture.CreateOrderHandler().Handle(Sale(ana, soap) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 40m, Method = OrderPaymentMethod.Cash }]
        }, default);
        Assert.True(withBalance.IsSuccess, withBalance.Error);
        Assert.True(inCash.IsSuccess, inCash.Error);
        var refund = await RefundHandler(fixture).Handle(new RefundOrderCommand
        {
            OrderId = inCash.Value.Id, Amount = 10m, Method = OrderPaymentMethod.CustomerBalance, Reason = "Ajuste", RestockItems = false
        }, default);
        Assert.True(refund.IsSuccess, refund.Error);
        var returned = await ReturnHandler(fixture).Handle(new ReturnCustomerBalanceCommand
        {
            CustomerId = ana.Id, Amount = 30m, Method = OrderPaymentMethod.Cash, ExpectedBalance = 160m
        }, default);
        Assert.True(returned.IsSuccess, returned.Error);

        var cut = await new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDailyCashCutQuery(), default);
        var shifts = (await new GetCashSessionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetCashSessionsQuery(), default)).Items;

        Assert.Equal((140m, 40m, 0m, 100m, 1, 10m), (cut.SalesPaymentsTotal, cut.CashTotal, cut.OtherTotal,
            cut.CustomerBalanceTotal, cut.CustomerBalancePaymentsCount, cut.CustomerBalanceRefundsTotal));
        Assert.Equal((250m, 30m, 200m, 30m), (cut.CustomerDepositsTotal, cut.CustomerDepositReturnsTotal,
            cut.CashCustomerDepositsTotal, cut.CashCustomerDepositReturnsTotal));
        Assert.Equal((0m, 0m), (cut.ManualIncomeTotal, cut.ManualExpenseTotal));
        // The money of the day is what the drawers took in: the sale paid with the balance entered when she left it.
        Assert.Equal(260m, cut.NetCashFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows + s.NonCashInflows), cut.NetCashFlow);
        Assert.Equal(210m, cut.NetCashOnlyFlow);
        Assert.Equal(shifts.Sum(s => s.Inflows - s.Outflows), cut.NetCashOnlyFlow);
        // Paid with what she left, the sale is no cashier's.
        Assert.Equal(40m, cut.ByUser.Sum(u => u.TotalAmount));
    }

    [Fact]
    public async Task CfdiAccountReceivablesAndCard_ShouldShowTheBalance()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, soap) = await ShopAsync(fixture);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 1000m);
        await fixture.OpenCashSessionAsync(tenant);
        await DepositHandler(fixture).Handle(Deposit(ana, 300m, OrderPaymentMethod.Cash), default);
        var withBalance = await fixture.CreateOrderHandler().Handle(BalanceSale(ana, soap, fromBalance: 100m), default);
        var onCredit = await fixture.CreateOrderHandler().Handle(CreditSale(ana, soap, quantity: 1), default);
        Assert.True(withBalance.IsSuccess, withBalance.Error);
        Assert.True(onCredit.IsSuccess, onCredit.Error);

        var draft = await new GetOrderCfdiDraftQueryHandler(fixture.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = withBalance.Value.Id }, default);
        Assert.True(draft.IsSuccess, draft.Error);
        Assert.Equal("30", draft.Value.PaymentForm);
        Assert.Contains(GetOrderCfdiDraftQueryHandler.CustomerBalanceWarning, draft.Value.Warnings);

        var receivables = await new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetReceivablesQuery(), default);
        var row = Assert.Single(receivables.Page.Items);
        Assert.Equal((100m, 200m, 200m), (row.Owed, row.CreditBalance, receivables.TotalCreditBalance));

        var card = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { CustomerId = ana.Id }, default);
        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal((100m, 200m), (card.Value.Balance, card.Value.CreditBalance));
    }

    [Fact]
    public void Balance_ShouldNotBeUsed_InPedidosPayments_OrDrawerMovementsByHand()
    {
        var payment = new AddOrderPaymentCommandValidator().Validate(
            new AddOrderPaymentCommand { OrderId = Guid.NewGuid(), Amount = 10m, Method = OrderPaymentMethod.CustomerBalance });
        var movement = new AddCashTransactionCommandValidator().Validate(new AddCashTransactionCommand
        {
            CashSessionId = Guid.NewGuid(), TransactionType = CashTransactionType.Deposit, Amount = 10m,
            PaymentMethod = OrderPaymentMethod.CustomerBalance
        });
        var byHand = new AddCashTransactionCommandValidator().Validate(new AddCashTransactionCommand
        {
            CashSessionId = Guid.NewGuid(), TransactionType = CashTransactionType.CustomerDeposit, Amount = 10m
        });

        Assert.Contains(OrderPaymentMethodRules.CustomerBalanceFromAccountMessage, payment.Errors.Select(e => e.ErrorMessage));
        Assert.Contains(OrderPaymentMethodRules.CustomerBalanceNeverEntersADrawerMessage, movement.Errors.Select(e => e.ErrorMessage));
        Assert.False(byHand.IsValid);
    }

    private static async Task<(Tenant Tenant, Product Soap)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        return (tenant, await fixture.AddProductAsync(tenant, "Jabón", 100m));
    }

    private static async Task<Customer> AddCustomerAsync(
        SalesTestFixture fixture, Tenant tenant, string name, string? phone, decimal? creditLimit = null)
    {
        var customer = new Customer(tenant.Id, name, phone, null);
        if (creditLimit is not null)
            customer.SetCreditLimit(creditLimit);
        fixture.Context.Customers.Add(customer);
        await fixture.Context.SaveChangesAsync();
        return customer;
    }

    private static AddCustomerDepositCommand Deposit(Customer customer, decimal amount, OrderPaymentMethod method, decimal? expected = null) => new()
    {
        CustomerId = customer.Id,
        Amount = amount,
        Method = method,
        ExpectedBalance = expected
    };

    private static CreateOrderCommand Sale(Customer customer, Product product) => new()
    {
        CustomerId = customer.Id,
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static CreateOrderCommand BalanceSale(Customer customer, Product product, decimal fromBalance) => Sale(customer, product) with
    {
        Payments = [new CreateOrderPaymentDto { Amount = fromBalance, Method = OrderPaymentMethod.CustomerBalance }]
    };

    private static CreateOrderCommand CreditSale(Customer customer, Product product, decimal quantity) => new()
    {
        CustomerId = customer.Id,
        OnCredit = true,
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }]
    };

    private static AddCustomerDepositCommandHandler DepositHandler(SalesTestFixture fixture) => new(
        fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
        new OrderCashService(fixture.Context, fixture.CurrentUser), new CustomerCreditLock(fixture.Context));

    private static ReturnCustomerBalanceCommandHandler ReturnHandler(SalesTestFixture fixture) => new(
        fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
        new OrderCashService(fixture.Context, fixture.CurrentUser), new CustomerCreditLock(fixture.Context));

    private static PayCustomerAccountCommandHandler PayHandler(SalesTestFixture fixture) => new(
        fixture.Context, fixture.TenantService, fixture.CurrentUser, new PhoneNormalizationService(), fixture.Clock,
        new OrderCashService(fixture.Context, fixture.CurrentUser), new CustomerCreditLock(fixture.Context));

    private static RefundOrderCommandHandler RefundHandler(SalesTestFixture fixture) => new(
        fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser), fixture.Clock,
        NoOpOrderInventoryService.Instance);

    private static VoidOrderCommandHandler VoidHandler(SalesTestFixture fixture) => new(
        fixture.Context, fixture.CurrentUser, new OrderCashService(fixture.Context, fixture.CurrentUser), fixture.Clock,
        NoOpOrderInventoryService.Instance);

    private static async Task<CustomerAccountDto> AccountAsync(SalesTestFixture fixture, Guid customerId)
    {
        var result = await new GetCustomerAccountQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.Clock)
            .Handle(new GetCustomerAccountQuery { CustomerId = customerId }, default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static IEnumerable<string> Errors(CreateOrderCommandValidator validator, CreateOrderCommand command)
        => validator.Validate(command).Errors.Select(e => e.ErrorMessage);
}
