using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The orders AI assistants prepare (Fase 32B, ADR 0096) on SQL Server: its link is found exactly although the column compares
/// without case; the order a customer sends from it and its confirmation are saved together; and two assistants preparing
/// orders for a business at once wait for each other on its lock.
/// </summary>
public class AssistantOrdersSqlServerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [SqlServerFact]
    public async Task TheLink_ShouldBeFoundExactly_ThoughTheDatabaseComparesItWithoutCase()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        var token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE";
        var swapped = "a" + token[1..];
        await using (var write = database.CreateContext(tenantId))
        {
            write.AssistantOrders.Add(Prepared(tenantId, token));
            await write.SaveChangesAsync();
        }

        await using var context = Context(database, out var tenant);
        // The column finds the other spelling too: only the exact link is its.
        Assert.Equal(1, await context.AssistantOrders.IgnoreQueryFilters().CountAsync(o => o.Token == swapped));
        var handler = new GetAssistantOrderQueryHandler(context, tenant, new Clock(Now));
        Assert.Equal(AssistantOrderState.Open, (await handler.Handle(new GetAssistantOrderQuery(token, "panaderia"), CancellationToken.None))!.State);
        Assert.Null(await handler.Handle(new GetAssistantOrderQuery(swapped, "panaderia"), CancellationToken.None));
        Assert.Null(await handler.Handle(new GetAssistantOrderQuery(token, "otra"), CancellationToken.None));
    }

    [SqlServerFact]
    public async Task TheOrderSentFromItsLink_ShouldBeSavedWithTheConfirmation_InTheSameSave()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, _) = await SeedAsync(database);
        var token = new string('k', 43);
        await using (var write = database.CreateContext(tenantId))
        {
            write.AssistantOrders.Add(Prepared(tenantId, token));
            await write.SaveChangesAsync();
        }

        Guid orderId;
        await using (var send = database.CreateContext(tenantId))
        {
            var order = new Order(tenantId, null, null, true, "ORD-000002", BusinessType.Food, OrderType.Retail, "Ana", "+525512345678", null, null);
            send.Orders.Add(order);
            (await send.AssistantOrders.SingleAsync()).Confirm(order.Id, Now.AddMinutes(5));
            await send.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var check = database.CreateContext(tenantId);
        var confirmed = await check.AssistantOrders.AsNoTracking().SingleAsync();
        Assert.Equal((AssistantOrderStatus.Confirmed, (Guid?)orderId), (confirmed.Status, confirmed.OrderId));
        Assert.Empty(confirmed.Lines);
    }

    [SqlServerFact]
    public async Task TwoPreparing_ShouldWaitForEachOther_OnTheLockOfTheirBusiness_AndNotOnAnother()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, otherId) = await SeedAsync(database);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        await using var third = database.CreateContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        await new AssistantOrdersLock(first).AcquireAsync(tenantId, CancellationToken.None);

        // Another business does not wait.
        await using (var otherTransaction = await third.Database.BeginTransactionAsync())
        {
            await new AssistantOrdersLock(third).AcquireAsync(otherId, CancellationToken.None);
            await otherTransaction.CommitAsync();
        }

        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var waiting = new AssistantOrdersLock(second).AcquireAsync(tenantId, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(waiting.IsCompleted);

        await firstTransaction.CommitAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await secondTransaction.CommitAsync();

        // It lasts as long as the request's transaction: outside one it is a mistake of the code.
        await using var outside = database.CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AssistantOrdersLock(outside).AcquireAsync(tenantId, CancellationToken.None));
    }

    /// <summary>A bakery that takes orders online and another business.</summary>
    private static async Task<(Guid TenantId, Guid OtherId)> SeedAsync(SqlServerTestDatabase database)
    {
        await using var seed = database.CreateContext();
        var bakery = new Tenant("Panadería", "panaderia", BusinessType.Food, "panaderia@demo.com");
        var other = new Tenant("Otra", "otra", BusinessType.Food, "otra@demo.com");
        var settings = new TenantSettings(bakery.Id, TenantCapability.Food);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        seed.AddRange(bakery, other, settings, new TenantSettings(other.Id, TenantCapability.Food),
            new Order(bakery.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Mariana", "+525522223333", null, null));
        await seed.SaveChangesAsync();
        return (bakery.Id, other.Id);
    }

    private static AssistantOrder Prepared(Guid tenantId, string token)
        => AssistantOrder.Prepare(tenantId, token, [new AccountCartLine(Guid.NewGuid(), [], 2, null)], null, 36m, "Claude", Now);

    /// <summary>A context with no business, and the service a handler puts one in.</summary>
    private static ApplicationDbContext Context(SqlServerTestDatabase database, out ICurrentTenantService tenant)
    {
        ICurrentTenantService? captured = null;
        var context = database.CreateContext<ApplicationDbContext>(null, (options, service, mediator) =>
        {
            captured = service;
            return new ApplicationDbContext(options, service, mediator);
        });
        tenant = captured!;
        return context;
    }

    private sealed class Clock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
