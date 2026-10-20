using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Fiado (Fase 26A) in SQL Server set up as in production: the receivables rule and its grouping by phone are read by the
/// database, and the customer's credit lock keeps sales on credit and account payments sent at the same time from passing
/// the limit or paying more than is owed. Fase 26B: what is overdue and what was forgiven are read by the database too, and
/// the same lock keeps a write-off and a payment at once from forgiving or paying more than is owed.
/// </summary>
public class CustomerCreditSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 17, 0, 0, TimeSpan.Zero);
    private const string AnaPhone = "+525512345678";

    [SqlServerFact]
    public async Task SalesOnCredit_AtTheSameTime_ShouldNeverPassTheLimit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 250m);
        // Ana bought before: her first sale creates her temporary customer, which would put the sales in line by itself.
        Assert.True((await shop.SellAsync(shop.CreditSale() with
        {
            OnCredit = false,
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash }]
        })).IsSuccess);

        var sales = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => shop.SellAsync(shop.CreditSale())));

        Assert.Equal(2, sales.Count(s => s.IsSuccess));
        Assert.All(sales.Where(s => s.IsFailure), s => Assert.Contains("su límite es $250.00", s.Error));
        Assert.Equal(200m, (await shop.AccountAsync()).Owed);
    }

    [SqlServerFact]
    public async Task TheSameSaleOnCredit_SentTwiceAtOnce_ShouldAnswerTheSaleRecorded_NotTheLimit()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 150m);
        Assert.True((await shop.SellAsync(shop.CreditSale() with
        {
            OnCredit = false,
            Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Cash }]
        })).IsSuccess);
        var key = Guid.NewGuid();

        // A retry of Vender keeps the key: the second one waits on Ana's lock while the first is recorded.
        var sales = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => shop.SellAsync(shop.CreditSale() with { IdempotencyKey = key })));

        Assert.All(sales, s => Assert.True(s.IsSuccess, s.Error));
        Assert.Single(sales.Select(s => s.Value.Id).Distinct());
        Assert.Equal(100m, (await shop.AccountAsync()).Owed);
    }

    [SqlServerFact]
    public async Task AccountPayments_AtTheSameTime_ShouldNeverPayMoreThanIsOwed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 500m);
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);

        var payments = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => shop.PayAsync(100m)));

        Assert.Equal(2, payments.Count(p => p.IsSuccess));
        Assert.Equal(PayCustomerAccountCommandHandler.NothingOwedMessage, Assert.Single(payments, p => p.IsFailure).Error);
        await using var check = database.CreateContext(shop.TenantId);
        Assert.Equal(200m, await check.OrderPayments.SumAsync(p => p.Amount));
        Assert.Equal(0m, (await shop.AccountAsync()).Owed);
        Assert.Equal(2, await check.Orders.CountAsync(o => o.SoldOnCredit && o.Status == OrderStatus.Delivered));
    }

    [SqlServerFact]
    public async Task Receivables_ShouldBeReadByTheDatabase_GroupedByPhone_HoweverItWasStored()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 150m);
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);
        // Ana's order from her chat (+521) handed over unpaid, Luis's sale saved unpaid and handed over, and an order of
        // Carla still in progress, which is not owed yet.
        var fromChat = await shop.SellAsync(shop.Sale("Ana", "+5215512345678") with { Channel = OrderChannel.WhatsApp });
        var luis = await shop.SellAsync(shop.Sale("Luis Pérez", "5566667777"));
        Assert.True((await shop.SellAsync(shop.Sale("Carla", "5577778888"))).IsSuccess);
        await shop.DeliverAsync(fromChat.Value.Id, luis.Value.Id);
        await using var context = database.CreateContext(shop.TenantId);
        var phones = new PhoneNormalizationService();
        var receivables = new GetReceivablesQueryHandler(context, phones, new Clock());

        var all = await receivables.Handle(new GetReceivablesQuery { Sort = ReceivablesSort.Oldest }, default);
        var byName = await receivables.Handle(new GetReceivablesQuery { Search = "LÓPEZ" }, default);
        var byDigits = await receivables.Handle(new GetReceivablesQuery { Search = "66 667" }, default);
        var account = await new GetCustomerAccountQueryHandler(context, phones, new Clock())
            .Handle(new GetCustomerAccountQuery { Phone = "+5215512345678" }, default);
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(shop.TenantId);
        var card = await new GetCustomerCardQueryHandler(context, phones, tenant, new Clock())
            .Handle(new GetCustomerCardQuery { Phone = "5566667777" }, default);

        Assert.Equal((300m, 2), (all.TotalOwed, all.Page.TotalCount));
        Assert.Equal([(AnaPhone, "Ana López", 200m, 2, true), ("+525566667777", "Luis Pérez", 100m, 1, false)],
            all.Page.Items.Select(i => (i.Phone, i.Name, i.Owed, i.Orders, i.OverLimit)));
        Assert.Equal(200m, Assert.Single(byName.Page.Items).Owed);
        Assert.Equal("Luis Pérez", Assert.Single(byDigits.Page.Items).Name);
        Assert.True(account.IsSuccess, account.Error);
        Assert.Equal((AnaPhone, 200m, 0m, true, 2), (account.Value.Phone, account.Value.Owed, account.Value.Available, account.Value.OverLimit, account.Value.Orders.Count));
        Assert.Equal(100m, card.Value.Balance);
    }

    [SqlServerFact]
    public async Task WriteOffAndPayment_AtTheSameTime_ShouldNeverTakeMoreThanIsOwed()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 500m);
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);

        // Each one waits on Ana's lock: whichever goes second finds the account changed.
        var writeOff = shop.WriteOffAsync(150m, expectedOwed: 200m);
        var payment = shop.PayAsync(100m);
        await Task.WhenAll(writeOff, payment);

        Assert.Equal(1, new[] { writeOff.Result.IsSuccess, payment.Result.IsSuccess }.Count(ok => ok));
        await using var check = database.CreateContext(shop.TenantId);
        var orders = await check.Orders.ToListAsync();
        Assert.All(orders, o => Assert.True(o.PaidAmount + o.WrittenOffAmount <= o.Total));
        Assert.Equal(writeOff.Result.IsSuccess ? 50m : 100m, (await shop.AccountAsync()).Owed);
    }

    [SqlServerFact]
    public async Task OverdueAndWriteOffs_ShouldBeReadByTheDatabase()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var shop = await Shop.OpenAsync(database, creditLimit: 500m);
        var old = await shop.SellAsync(shop.CreditSale());
        Assert.True((await shop.SellAsync(shop.CreditSale())).IsSuccess);
        var luis = await shop.SellAsync(shop.Sale("Luis Pérez", "5566667777"));
        await shop.DeliverAsync(luis.Value.Id);
        await using (var context = database.CreateContext(shop.TenantId))
        {
            // Sold on 9 Oct in Mexico City: with 7 days it was due on 16 Oct, overdue on the 19th.
            await context.Database.ExecuteSqlAsync(
                $"UPDATE [Orders] SET [CreatedAt] = {new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.FromHours(-6))} WHERE [Id] = {old.Value.Id}");
            // Luis's was made on 1 Oct but handed over unpaid on 18 Oct: its term counts from the 18th, so nothing is overdue.
            await context.Database.ExecuteSqlAsync(
                $"UPDATE [Orders] SET [CreatedAt] = {new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(-6))} WHERE [Id] = {luis.Value.Id}");
            await context.Database.ExecuteSqlAsync(
                $"UPDATE [OrderStatusHistory] SET [ChangedAt] = {new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(-6))} WHERE [OrderId] = {luis.Value.Id} AND [NewStatus] = {(int)OrderStatus.Delivered}");
            (await context.TenantSettings.SingleAsync()).SetCreditTermDays(7);
            await context.SaveChangesAsync();
        }
        var forgiven = await shop.WriteOffAsync(30m, expectedOwed: 200m);
        Assert.True(forgiven.IsSuccess, forgiven.Error);

        await using var read = database.CreateContext(shop.TenantId);
        var receivables = new GetReceivablesQueryHandler(read, new PhoneNormalizationService(), new Clock());
        var all = await receivables.Handle(new GetReceivablesQuery(), default);
        var overdue = await receivables.Handle(new GetReceivablesQuery { OverdueOnly = true, Search = "ana" }, default);
        var account = await shop.AccountAsync();

        Assert.Equal((270m, 70m, (int?)7), (all.TotalOwed, all.TotalOverdue, all.CreditTermDays));
        Assert.Equal([(AnaPhone, 170m, 70m), ("+525566667777", 100m, 0m)], all.Page.Items.Select(i => (i.Phone, i.Owed, i.Overdue)));
        Assert.Equal((AnaPhone, 170m, 70m), Assert.Single(overdue.Page.Items) is var row ? (row.Phone, row.Owed, row.Overdue) : default);
        Assert.Equal((170m, 70m, 7), (account.Owed, account.Overdue, account.CreditTermDays!.Value));
        Assert.Equal(
            [(new DateOnly(2026, 10, 16), true, 30m, 70m), (new DateOnly(2026, 10, 26), false, 0m, 100m)],
            account.Orders.Select(o => (o.DueOn!.Value, o.IsOverdue, o.WrittenOff, o.Balance)));
        var record = Assert.Single(account.RecentWriteOffs);
        Assert.Equal((30m, OrderWriteOffReason.Courtesy, Now), (record.Amount, record.Reason, record.WrittenOffAt));
    }

    /// <summary>A shop that sells soap at $100 (IVA included), with an open drawer and Ana, who may owe the limit given.</summary>
    private sealed class Shop
    {
        private readonly SqlServerTestDatabase _database;

        private Shop(SqlServerTestDatabase database, Guid tenantId, Guid productId, Guid customerId)
        {
            _database = database;
            TenantId = tenantId;
            ProductId = productId;
            CustomerId = customerId;
        }

        public Guid TenantId { get; }
        public Guid ProductId { get; }
        public Guid CustomerId { get; }

        public static async Task<Shop> OpenAsync(SqlServerTestDatabase database, decimal creditLimit)
        {
            var slug = Guid.NewGuid().ToString("N")[..10];
            var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
            var settings = new TenantSettings(tenant.Id, TenantCapability.Retail);
            settings.UpdateSalesPolicy(pricesIncludeTax: true, defaultTaxRate: 16m, autoCloseCounterSales: true);
            var branch = new Branch(tenant.Id, "Matriz", $"MATRIZ-{slug}", isDefault: true);
            var category = new Category(tenant.Id, "General", null);
            var product = new Product(tenant.Id, category.Id, "Jabón", null, 100m, "MXN");
            var ana = new Customer(tenant.Id, "Ana López", AnaPhone, null);
            ana.SetCreditLimit(creditLimit);

            await using var context = database.CreateContext(tenant.Id);
            context.Tenants.Add(tenant);
            context.TenantSettings.Add(settings);
            context.Branches.Add(branch);
            context.InventoryLocations.Add(new InventoryLocation(tenant.Id, "Mostrador", $"MAIN-{slug}", isDefault: true, branchId: branch.Id));
            context.Categories.Add(category);
            context.Products.Add(product);
            context.Customers.Add(ana);
            context.CashSessions.Add(new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m));
            await context.SaveChangesAsync();

            return new Shop(database, tenant.Id, product.Id, ana.Id);
        }

        public CreateOrderCommand CreditSale() => new()
        {
            CustomerId = CustomerId,
            OnCredit = true,
            Items = [new CreateOrderItemDto { ProductId = ProductId, Quantity = 1 }]
        };

        public CreateOrderCommand Sale(string name, string phone) => new()
        {
            CustomerName = name,
            CustomerPhone = phone,
            Items = [new CreateOrderItemDto { ProductId = ProductId, Quantity = 1 }]
        };

        /// <summary>Sends the sale through the transaction of the pipeline, with the services of production.</summary>
        public async Task<Result<OrderDto>> SellAsync(CreateOrderCommand command)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            var folios = new FolioGenerator(context);
            var phones = new PhoneNormalizationService();
            var handler = new CreateOrderCommandHandler(
                context,
                tenant,
                user,
                new CustomerLookupService(context, phones, NullLogger<CustomerLookupService>.Instance),
                clock,
                phones,
                orderInventoryService: new OrderInventoryService(context, clock, folios, NullLogger<OrderInventoryService>.Instance),
                orderCashService: new OrderCashService(context, user),
                folios: folios,
                creditLock: new CustomerCreditLock(context));

            return await new TransactionBehavior<CreateOrderCommand, Result<OrderDto>>(context)
                .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
        }

        /// <summary>A cash payment to Ana's account, through the transaction of the pipeline.</summary>
        public async Task<Result<CustomerAccountDto>> PayAsync(decimal amount)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            var command = new PayCustomerAccountCommand { CustomerId = CustomerId, Amount = amount, Method = OrderPaymentMethod.Cash };
            var handler = new PayCustomerAccountCommandHandler(
                context, tenant, user, new PhoneNormalizationService(), clock, new OrderCashService(context, user),
                new CustomerCreditLock(context));

            return await new TransactionBehavior<PayCustomerAccountCommand, Result<CustomerAccountDto>>(context)
                .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
        }

        /// <summary>A write-off of Ana's account by the owner, through the transaction of the pipeline.</summary>
        public async Task<Result<CustomerAccountDto>> WriteOffAsync(decimal amount, decimal expectedOwed)
        {
            var (tenant, user, clock) = Request();
            await using var context = Context(tenant, user, clock);
            var command = new WriteOffCustomerAccountCommand
            {
                CustomerId = CustomerId,
                Amount = amount,
                Reason = OrderWriteOffReason.Courtesy,
                ExpectedOwed = expectedOwed
            };
            var handler = new WriteOffCustomerAccountCommandHandler(
                context, tenant, user, new PhoneNormalizationService(), clock, new CustomerCreditLock(context));

            return await new TransactionBehavior<WriteOffCustomerAccountCommand, Result<CustomerAccountDto>>(context)
                .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
        }

        public async Task<CustomerAccountDto> AccountAsync()
        {
            await using var context = _database.CreateContext(TenantId);
            var account = await new GetCustomerAccountQueryHandler(context, new PhoneNormalizationService(), new Clock())
                .Handle(new GetCustomerAccountQuery { CustomerId = CustomerId }, default);
            Assert.True(account.IsSuccess, account.Error);
            return account.Value;
        }

        public async Task DeliverAsync(params Guid[] orderIds)
        {
            await using var context = _database.CreateContext(TenantId);
            var machine = OrderStateMachineFactory.Create(BusinessType.Retail, OrderType.Retail);
            foreach (var order in await context.Orders.Where(o => orderIds.Contains(o.Id)).ToListAsync())
                order.TransitionTo(OrderStatus.Delivered, machine, "cajero");
            await context.SaveChangesAsync();
        }

        private (InMemoryApplicationDbContext.TestCurrentTenantService Tenant, Cashier User, Clock Clock) Request()
        {
            var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
            tenant.SetTenant(TenantId);
            return (tenant, new Cashier(), new Clock());
        }

        private Persistence.ApplicationDbContext Context(ICurrentTenantService tenant, Cashier user, Clock clock)
            => _database.CreateProductionLikeContext(
                tenant,
                new InMemoryApplicationDbContext.NullMediator(),
                new AuditableEntityInterceptor(user, clock),
                new TenantSaveChangesInterceptor(tenant));
    }

    private sealed class Cashier : ICurrentUserService
    {
        public string? UserId => "4f6c2a0e-8d8b-4a8f-9a55-0c4b1f3f8a10";
        public string? UserName => "cajero";
        public string? Role => "TENANT_ADMIN";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }
}
