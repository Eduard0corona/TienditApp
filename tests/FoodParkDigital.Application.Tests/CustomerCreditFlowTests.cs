using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Fiado (Fase 26A, ADR 0033): the owner gives a customer a limit, Vender sells on credit within it, Clientes › Por cobrar
/// lists who owes, the account shows what each one owes, and a payment to the account is split from the oldest order.
/// </summary>
public class CustomerCreditFlowTests
{
    private const string AnaPhone = "+525512345678";

    [Fact]
    public async Task CreditLimit_ShouldBeTheOwnersDecision_Recorded_AndRefusedForWhoCannotOwe()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone);
        var walkIn = await AddCustomerAsync(fixture, tenant, "Público en general", null, isPublicGeneral: true);
        var handler = new UpdateCustomerCreditLimitCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var given = await handler.Handle(new UpdateCustomerCreditLimitCommand { CustomerId = ana.Id, CreditLimit = 500m }, default);
        var again = await handler.Handle(new UpdateCustomerCreditLimitCommand { CustomerId = ana.Id, CreditLimit = 500m }, default);
        var refused = await handler.Handle(new UpdateCustomerCreditLimitCommand { CustomerId = walkIn.Id, CreditLimit = 500m }, default);

        Assert.True(given.IsSuccess, given.Error);
        Assert.Equal(500m, given.Value.CreditLimit);
        Assert.True(again.IsSuccess);
        Assert.Equal(Customer.PublicGeneralCreditMessage, refused.Error);
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerCreditLimitUpdated").ToListAsync());
        Assert.Equal(ana.Id.ToString(), log.EntityId);
        Assert.Contains("\"New\":500", log.Metadata);
        Assert.True(typeof(UpdateCustomerCreditLimitCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));

        var validator = new UpdateCustomerCreditLimitCommandValidator();
        Assert.False(validator.Validate(new UpdateCustomerCreditLimitCommand { CustomerId = ana.Id, CreditLimit = 0.5m }).IsValid);
        Assert.False(validator.Validate(new UpdateCustomerCreditLimitCommand { CustomerId = ana.Id, CreditLimit = 1_000_000.01m }).IsValid);
        Assert.True(validator.Validate(new UpdateCustomerCreditLimitCommand { CustomerId = ana.Id, CreditLimit = null }).IsValid);
    }

    [Fact]
    public async Task SaleOnCredit_ShouldBeHandedOver_WithWhatItLeavesOwed_InTheAccountAndTheCard()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        var session = await fixture.OpenCashSessionAsync(tenant);

        var sale = await fixture.CreateOrderHandler().Handle(CreditSale(ana, soap, quantity: 2) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 50m, Method = OrderPaymentMethod.Cash }]
        }, default);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.True(sale.Value.SoldOnCredit);
        Assert.Equal(OrderStatus.Delivered, sale.Value.Status);
        Assert.Equal(150m, sale.Value.Balance);
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.Equal(50m, drawer.Transactions.Where(t => t.TransactionType == CashTransactionType.SalePayment).Sum(t => t.Amount));

        var account = await AccountAsync(fixture, ana.Id);
        Assert.Equal((150m, 350m, 1, true), (account.Owed, account.Available, account.OrdersCount, account.SellsOnCredit));
        Assert.True(Assert.Single(account.Orders).SoldOnCredit);
        Assert.Equal(50m, Assert.Single(account.RecentPayments).Amount);

        var card = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { CustomerId = ana.Id }, default);
        Assert.Equal((150m, 500m, true), (card.Value.Balance, card.Value.CreditLimit, card.Value.SellsOnCredit));
    }

    [Fact]
    public async Task SaleOnCredit_ShouldStopAtTheLimit_SayingHowMuchToCharge()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 250m);
        await fixture.OpenCashSessionAsync(tenant);
        var handler = fixture.CreateOrderHandler();
        Assert.True((await handler.Handle(CreditSale(ana, soap, quantity: 2), default)).IsSuccess);

        var over = await handler.Handle(CreditSale(ana, soap, quantity: 1), default);
        var withPayment = await handler.Handle(CreditSale(ana, soap, quantity: 1) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 50m, Method = OrderPaymentMethod.Card }]
        }, default);

        Assert.Equal(CustomerCredit.OverLimitMessage("Ana López", 200m, 100m, 250m), over.Error);
        Assert.Contains("cobra al menos $50.00", over.Error);
        Assert.True(withPayment.IsSuccess, withPayment.Error);
        Assert.Equal(250m, (await AccountAsync(fixture, ana.Id)).Owed);
        // The refused sale left nothing behind.
        Assert.Equal(2, await fixture.Context.Orders.CountAsync());
    }

    [Fact]
    public async Task SaleOnCredit_ShouldNeed_ACustomerWithCredit_ACounterSale_AndChargeOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var noLimit = await AddCustomerAsync(fixture, tenant, "Mario", "+525522223333");
        var inactive = await AddCustomerAsync(fixture, tenant, "Rosa", "+525544445555", creditLimit: 500m);
        inactive.SetActive(false);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        await fixture.OpenCashSessionAsync(tenant);
        var handler = fixture.CreateOrderHandler();

        Assert.Equal(CustomerCredit.NoCreditMessage("Mario"), (await handler.Handle(CreditSale(noLimit, soap), default)).Error);
        Assert.Equal(CustomerCredit.NoCreditMessage("Rosa"), (await handler.Handle(CreditSale(inactive, soap), default)).Error);
        // The customer's record, sold to another phone: what is owed goes by the phone, so it is refused.
        var otherPhone = await handler.Handle(CreditSale(ana, soap) with { CustomerPhone = "+525599998888" }, default);
        Assert.Equal(CustomerCredit.PhoneIsNotTheCustomersMessage, otherPhone.Error);
        // Paid in full: nothing to sell on credit.
        var paid = await handler.Handle(CreditSale(ana, soap) with
        {
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash }]
        }, default);
        Assert.Equal(Order.CreditNeedsBalanceMessage, paid.Error);

        var validator = new CreateOrderCommandValidator();
        var counter = CreditSale(ana, soap);
        Assert.True(validator.Validate(counter).IsValid);
        Assert.Contains(Order.CreditNeedsCustomerMessage, Errors(validator, counter with { CustomerId = null }));
        Assert.Contains(Order.CreditOnlyCounterSaleMessage, Errors(validator, counter with { OpenTab = true, TableNumber = "4" }));
        Assert.Contains(Order.CreditOnlyCounterSaleMessage, Errors(validator, counter with { CreatedByBusiness = false }));
        Assert.Contains(CreateOrderCommandValidator.CreditWithConnectionMessage,
            Errors(validator, counter with { IdempotencyKey = Guid.NewGuid(), CapturedOfflineAt = SalesTestFixture.Now }));
        Assert.Contains(TenantPermission.ChargeOrders, counter.RequiredPermissions());
        Assert.DoesNotContain(TenantPermission.ChargeOrders, (counter with { OnCredit = false }).RequiredPermissions());
    }

    [Fact]
    public async Task FoodOnCredit_ShouldBeOwed_WhileTheKitchenPreparesIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 80m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        await fixture.OpenCashSessionAsync(tenant);

        var sale = await fixture.CreateOrderHandler().Handle(CreditSale(ana, tacos), default);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.NotEqual(OrderStatus.Delivered, sale.Value.Status);
        Assert.Equal(80m, (await AccountAsync(fixture, ana.Id)).Owed);
    }

    [Fact]
    public async Task AccountPayment_ShouldSplitFromTheOldestOrder_IntoTheDrawer_WithTheChangeOnTheLastPart()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var smallSoap = await fixture.AddProductAsync(tenant, "Jabón chico", 50m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        var session = await fixture.OpenCashSessionAsync(tenant);
        var handler = fixture.CreateOrderHandler();
        var newer = await handler.Handle(CreditSale(ana, smallSoap), default);
        var older = await handler.Handle(CreditSale(ana, soap), default);
        Assert.True(older.IsSuccess && newer.IsSuccess, older.Error ?? newer.Error);
        await StampAsync(fixture, older.Value.Id, SalesTestFixture.Now.AddDays(-2));
        await StampAsync(fixture, newer.Value.Id, SalesTestFixture.Now.AddDays(-1));

        var paid = await PayHandler(fixture).Handle(new PayCustomerAccountCommand
        {
            CustomerId = ana.Id,
            Amount = 120m,
            Method = OrderPaymentMethod.Cash,
            CashReceived = 200m
        }, default);

        Assert.True(paid.IsSuccess, paid.Error);
        Assert.Equal(30m, paid.Value.Owed);
        Assert.Equal(newer.Value.Id, Assert.Single(paid.Value.Orders).OrderId);
        var payments = await fixture.Context.OrderPayments.OrderBy(p => p.Amount).ToListAsync();
        Assert.Equal([(20m, newer.Value.Id, 100m), (100m, older.Value.Id, (decimal?)null)],
            payments.Select(p => (p.Amount, p.OrderId, p.CashReceived)));
        Assert.Equal(80m, payments.Sum(p => p.Change));
        Assert.All(payments, p => Assert.Equal(PayCustomerAccountCommand.DefaultNote, p.Notes));
        var drawer = await fixture.Context.CashSessions.Include(s => s.Transactions).SingleAsync(s => s.Id == session.Id);
        Assert.Equal(120m, drawer.Transactions.Where(t => t.TransactionType == CashTransactionType.SalePayment).Sum(t => t.Amount));
        var log = Assert.Single(await fixture.Context.AuditLogs.Where(l => l.Action == "CustomerAccountPaid").ToListAsync());
        Assert.Contains(older.Value.OrderNumber, log.Metadata);
        Assert.DoesNotContain(AnaPhone, log.Metadata);
    }

    [Fact]
    public async Task AccountPayment_ShouldBeRefused_AboveWhatIsOwed_WithNothingOwed_OrWithoutADrawer()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        var mario = await AddCustomerAsync(fixture, tenant, "Mario", "+525522223333");
        var session = await fixture.OpenCashSessionAsync(tenant);
        Assert.True((await fixture.CreateOrderHandler().Handle(CreditSale(ana, soap), default)).IsSuccess);
        var handler = PayHandler(fixture);

        var above = await handler.Handle(Pay(ana, 100.01m), default);
        var nothing = await handler.Handle(Pay(mario, 10m), default);
        session.Close(SalesTestFixture.Now, "cajero", session.ExpectedBalance);
        await fixture.Context.SaveChangesAsync();
        var noDrawer = await handler.Handle(Pay(ana, 10m), default);

        Assert.Equal(PayCustomerAccountCommandHandler.AboveOwedMessage(100m), above.Error);
        Assert.Equal(PayCustomerAccountCommandHandler.NothingOwedMessage, nothing.Error);
        Assert.Equal(OrderPaymentRegistrar.NoOpenDrawerMessage, noDrawer.Error);
        Assert.Empty(await fixture.Context.OrderPayments.ToListAsync());

        var validator = new PayCustomerAccountCommandValidator();
        Assert.False(validator.Validate(Pay(ana, 10m) with { Method = OrderPaymentMethod.Platform }).IsValid);
        Assert.False(validator.Validate(Pay(ana, 10m) with { Method = OrderPaymentMethod.Card, CashReceived = 20m }).IsValid);
        Assert.False(validator.Validate(Pay(ana, 10m) with { CustomerId = null }).IsValid);
    }

    [Fact]
    public async Task AccountPayment_ThatSettlesAFoodSale_ShouldLeaveItWithTheKitchen()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Kitchen, pricesIncludeTax: true);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 80m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 500m);
        await fixture.OpenCashSessionAsync(tenant);
        var sale = await fixture.CreateOrderHandler().Handle(CreditSale(ana, tacos), default);

        var paid = await PayHandler(fixture).Handle(Pay(ana, 80m), default);

        Assert.True(paid.IsSuccess, paid.Error);
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Value.Id);
        Assert.Equal((0m, true), (order.Balance, order.SoldOnCredit));
        Assert.NotEqual(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public async Task Receivables_ShouldList_WhoOwes_ByPhone_WithTheRuleOfWhatIsOwed()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var soap = await fixture.AddProductAsync(tenant, "Jabón", 100m);
        var smallSoap = await fixture.AddProductAsync(tenant, "Jabón chico", 50m);
        var ana = await AddCustomerAsync(fixture, tenant, "Ana López", AnaPhone, creditLimit: 150m);
        await fixture.OpenCashSessionAsync(tenant);
        var handler = fixture.CreateOrderHandler();
        var machine = OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail);

        // Ana: a sale on credit, and an order from her chat (+521) delivered unpaid; the owner lowered her limit since.
        var onCredit = await handler.Handle(CreditSale(ana, soap), default);
        await StampAsync(fixture, onCredit.Value.Id, SalesTestFixture.Now);
        var fromChat = await handler.Handle(Sale("Ana", "+5215512345678", soap) with { Channel = OrderChannel.WhatsApp }, default);
        await DeliverAsync(fixture, fromChat.Value.Id, machine);
        await StampAsync(fixture, fromChat.Value.Id, SalesTestFixture.Now.AddDays(-3));
        // Luis, without a customer record: a sale saved unpaid and handed over.
        var luis = await handler.Handle(Sale("Luis Pérez", "5566667777", smallSoap), default);
        await DeliverAsync(fixture, luis.Value.Id, machine);
        await StampAsync(fixture, luis.Value.Id, SalesTestFixture.Now.AddDays(-1));
        // Not owed: an order still in progress, and a cancelled one.
        await handler.Handle(Sale("Carla", "5577778888", soap), default);
        var cancelled = await handler.Handle(Sale("Pepe", "5588889999", soap), default);
        (await fixture.Context.Orders.SingleAsync(o => o.Id == cancelled.Value.Id)).Cancel(machine, "test", "error");
        await fixture.Context.SaveChangesAsync();
        var receivables = new GetReceivablesQueryHandler(fixture.Context, new PhoneNormalizationService());

        var byOwed = await receivables.Handle(new GetReceivablesQuery(), default);
        var oldest = await receivables.Handle(new GetReceivablesQuery { Sort = ReceivablesSort.Oldest }, default);
        var byName = await receivables.Handle(new GetReceivablesQuery { Search = "lópez" }, default);
        var byDigits = await receivables.Handle(new GetReceivablesQuery { Search = "6666" }, default);

        Assert.Equal(250m, byOwed.TotalOwed);
        Assert.Equal(2, byOwed.Page.TotalCount);
        var first = byOwed.Page.Items[0];
        Assert.Equal((AnaPhone, "Ana López", ana.Id, 200m, 2, 150m, true),
            (first.Phone, first.Name, first.CustomerId, first.Owed, first.Orders, first.CreditLimit, first.OverLimit));
        Assert.Equal(SalesTestFixture.Now.AddDays(-3), first.Since);
        var second = byOwed.Page.Items[1];
        Assert.Equal(("+525566667777", "Luis Pérez", (Guid?)null, 50m, false), (second.Phone, second.Name, second.CustomerId, second.Owed, second.OverLimit));
        Assert.Equal([AnaPhone, "+525566667777"], oldest.Page.Items.Select(i => i.Phone));
        await StampAsync(fixture, fromChat.Value.Id, SalesTestFixture.Now);
        var nowOldest = await receivables.Handle(new GetReceivablesQuery { Sort = ReceivablesSort.Oldest }, default);
        Assert.Equal(["+525566667777", AnaPhone], nowOldest.Page.Items.Select(i => i.Phone));
        // A customer who matches keeps everything they owe.
        Assert.Equal(200m, Assert.Single(byName.Page.Items).Owed);
        Assert.Equal("Luis Pérez", Assert.Single(byDigits.Page.Items).Name);
    }

    private static async Task<Customer> AddCustomerAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string name,
        string? phone,
        decimal? creditLimit = null,
        bool isPublicGeneral = false)
    {
        var customer = new Customer(tenant.Id, name, phone, null, isPublicGeneral: isPublicGeneral);
        if (creditLimit is not null)
            customer.SetCreditLimit(creditLimit);
        fixture.Context.Customers.Add(customer);
        await fixture.Context.SaveChangesAsync();
        return customer;
    }

    private static CreateOrderCommand CreditSale(Customer customer, Product product, decimal quantity = 1) => new()
    {
        CustomerId = customer.Id,
        OnCredit = true,
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = quantity }]
    };

    private static CreateOrderCommand Sale(string name, string phone, Product product) => new()
    {
        CustomerName = name,
        CustomerPhone = phone,
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    /// <summary>When the order was made (the in-memory context leaves the date to the test).</summary>
    private static async Task StampAsync(SalesTestFixture fixture, Guid orderId, DateTimeOffset createdAt)
    {
        (await fixture.Context.Orders.SingleAsync(o => o.Id == orderId)).CreatedAt = createdAt;
        await fixture.Context.SaveChangesAsync();
    }

    private static PayCustomerAccountCommand Pay(Customer customer, decimal amount) => new()
    {
        CustomerId = customer.Id,
        Amount = amount,
        Method = OrderPaymentMethod.Cash
    };

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
        var result = await new GetCustomerAccountQueryHandler(fixture.Context, new PhoneNormalizationService())
            .Handle(new GetCustomerAccountQuery { CustomerId = customerId }, default);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static async Task DeliverAsync(SalesTestFixture fixture, Guid orderId, IOrderStateMachine machine)
    {
        (await fixture.Context.Orders.SingleAsync(o => o.Id == orderId)).TransitionTo(OrderStatus.Delivered, machine, "test");
        await fixture.Context.SaveChangesAsync();
    }

    private static IEnumerable<string> Errors(CreateOrderCommandValidator validator, CreateOrderCommand command)
        => validator.Validate(command).Errors.Select(e => e.ErrorMessage);
}
