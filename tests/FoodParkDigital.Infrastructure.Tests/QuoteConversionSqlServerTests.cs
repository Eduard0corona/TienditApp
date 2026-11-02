using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence.Interceptors;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The accepted quote becomes its service order on SQL Server (Fase 29E, ADR 0053 point 17), inside the transaction of the
/// pipeline: «Convertir en orden de servicio» pressed four times at once leaves one order, since the quote's version makes
/// the others collide and undoes each of them whole, its folio and lines included.
/// </summary>
public class QuoteConversionSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task ConvertingAtOnce_ShouldLeaveOneOrder_AndTellTheRestItWasConverted()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, quoteId) = await SeedAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ConvertAsync(database, tenantId, quoteId)));

        var converted = Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(QuoteRules.AlreadyConvertedMessage, r.Error));
        await using var check = database.CreateContext(tenantId);
        var order = await check.Orders.Include(o => o.Items).SingleAsync();
        var quote = await check.Quotes.Include(q => q.Lines).SingleAsync();
        Assert.Equal((order.Id, order.Id), (quote.OrderId, converted.Value!.Order!.Id));
        Assert.Equal((OrderType.Service, OrderStatus.Accepted, 1160m), (order.OrderType, order.Status, order.Total));
        // The line written by hand keeps the line's id where a product would go.
        var item = Assert.Single(order.Items);
        Assert.Equal((Assert.Single(quote.Lines).Id, "Cambio de calentador"), (item.ProductId, item.ProductName));
    }

    [SqlServerFact]
    public async Task ConvertingWithTheVisitAtOnce_ShouldTakeItsMoneyOnce_AndLeaveTheBalanceAsItWas()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, quoteId, visitSaleId) = await SeedWithVisitAsync(database);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => ConvertAsync(database, tenantId, quoteId)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(QuoteRules.AlreadyConvertedMessage, r.Error));
        await using var check = database.CreateContext(tenantId);
        var visitSale = await check.Orders.Include(o => o.Refunds).SingleAsync(o => o.Id == visitSaleId);
        var job = await check.Orders.Include(o => o.Payments).SingleAsync(o => o.Id != visitSaleId);
        // The visit's 406 left its sale once and paid the job once; the balance in favor ends at zero.
        Assert.Equal((406m, 0m), (Assert.Single(visitSale.Refunds).Amount, visitSale.NetPaidAmount));
        Assert.Equal((OrderPaymentMethod.CustomerBalance, 406m, 754m), (Assert.Single(job.Payments).Method, job.PaidAmount, job.Balance));
        var entries = await check.CustomerBalanceEntries.ToListAsync();
        Assert.Equal((2, 0m), (entries.Count, entries.Sum(e => e.Amount)));
        Assert.Equal(406m, (await check.Quotes.SingleAsync()).AppliedVisitCredit);
    }

    [SqlServerFact]
    public async Task ARefundOfTheVisitAtTheSameTime_ShouldNotLetItsMoneyAlsoPayTheJob()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, quoteId, visitSaleId) = await SeedWithVisitAsync(database);

        // The business gives the visit back in cash while the quote is converted: one of them stands.
        var refund = RefundVisitAsync(database, tenantId, visitSaleId);
        var conversion = ConvertAsync(database, tenantId, quoteId);
        await Task.WhenAll(refund, conversion);

        await using var check = database.CreateContext(tenantId);
        var visitSale = await check.Orders.Include(o => o.Refunds).SingleAsync(o => o.Id == visitSaleId);
        Assert.Equal(406m, visitSale.Refunds.Sum(r => r.Amount));
        Assert.True(visitSale.NetPaidAmount >= 0);
        var balance = await check.CustomerBalanceEntries.SumAsync(e => (decimal?)e.Amount) ?? 0m;
        Assert.Equal(0m, balance);
        if ((await conversion).IsSuccess)
            Assert.Equal(406m, (await check.Orders.Include(o => o.Payments).SingleAsync(o => o.Id != visitSaleId)).PaidAmount);
        else
            Assert.Equal(QuoteConversions.ChangedMeanwhileMessage, (await conversion).Error);
    }

    /// <summary>The business refunds the 406 of the visit's sale in cash, through the transaction of the pipeline.</summary>
    private static async Task<bool> RefundVisitAsync(SqlServerTestDatabase database, Guid tenantId, Guid visitSaleId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var user = new Owner();
        var clock = new Clock();
        var sale = await context.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .SingleAsync(o => o.Id == visitSaleId);
        try
        {
            sale.RegisterRefund(406m, OrderPaymentMethod.Cash, "No se hizo el trabajo", clock.UtcNow, "dueño", null, null, false, []);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException or Domain.Exceptions.DomainException)
        {
            return false;
        }
    }

    /// <summary>One «Convertir en orden de servicio» in its own context, through the transaction of the pipeline.</summary>
    private static async Task<Result<QuoteDto>> ConvertAsync(SqlServerTestDatabase database, Guid tenantId, Guid quoteId)
    {
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        await using var context = database.CreateProductionLikeContext(
            tenant, new InMemoryApplicationDbContext.NullMediator(), new TenantSaveChangesInterceptor(tenant));
        var user = new Owner();
        var phones = new PhoneNormalizationService();
        var clock = new Clock();
        var handler = new ConvertQuoteToOrderCommandHandler(
            context, tenant, user, new CurrentAccessContext(user, tenant, new Managers()),
            new CustomerLookupService(context, phones, NullLogger<CustomerLookupService>.Instance), phones,
            new OrderInventoryService(context, clock, new FolioGenerator(context), NullLogger<OrderInventoryService>.Instance),
            new OrderCashService(context, user), new FolioGenerator(context), clock);
        var command = new ConvertQuoteToOrderCommand(quoteId, null);
        return await new TransactionBehavior<ConvertQuoteToOrderCommand, Result<QuoteDto>>(context)
            .Handle(command, () => handler.Handle(command, CancellationToken.None), CancellationToken.None);
    }

    /// <summary>A trade with a quote the customer accepted today.</summary>
    private static async Task<(Guid TenantId, Guid QuoteId)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), null, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        // The business's day: Mexico City, six hours behind.
        var today = DateOnly.FromDateTime(Now.AddHours(-6).DateTime);
        quote.Send(today, new string('q', 43), null, Now);
        quote.Accept(today, QuoteReplyChannel.WhatsApp, null, Now);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, quote);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote.Id);
    }

    /// <summary>
    /// A trade whose visit «Se descuenta si contratas» was charged 406 (its service, with IVA inside) to Valeria, and the quote
    /// of that visit for 1,160, accepted, which takes the 406 off.
    /// </summary>
    private static async Task<(Guid TenantId, Guid QuoteId, Guid VisitSaleId)> SeedWithVisitAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Plomería {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
        var settings = new TenantSettings(tenant.Id, TenantCapability.Service | TenantCapability.Appointments);
        var service = Guid.NewGuid();
        var visit = new Appointment(tenant.Id, "Valeria", "5522107765", "Visita y diagnóstico", Now.AddDays(-1), Now.AddDays(-1).AddHours(1),
            serviceProductId: service);
        var visitSale = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Service, OrderType.Service, "Valeria",
            "+525522107765", null, null, pricesIncludeTax: true);
        visitSale.AddItem(service, "Visita y diagnóstico", 1, 406m);
        visitSale.RegisterPayment(406m, OrderPaymentMethod.Cash, Now.AddDays(-1), null, null, null, null);
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(1), new QuoteCustomer(null, "Valeria", "5522107765", null), visit.Id, true, 15, Now);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now);
        quote.SetVisitCredit(new QuoteVisitCredit(visitSale.Id, 406m), Now);
        var today = DateOnly.FromDateTime(Now.AddHours(-6).DateTime);
        quote.Send(today, new string('v', 43), null, Now);
        quote.Accept(today, QuoteReplyChannel.WhatsApp, null, Now);
        await using var seed = database.CreateContext(tenant.Id);
        seed.AddRange(tenant, settings, visit, visitSale, quote);
        await seed.SaveChangesAsync();
        return (tenant.Id, quote.Id, visitSale.Id);
    }

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Owner : ICurrentUserService
    {
        public string? UserId { get; } = Guid.NewGuid().ToString();
        public string? UserName => "dueño";
        public string? Role => "TenantAdmin";
        public string? TenantRole => "Owner";
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }

    /// <summary>The owner: every permission.</summary>
    private sealed class Managers : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Service | TenantCapability.Appointments, [], IsManager: true));

        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>(Enum.GetNames<TenantPermission>());

        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}
