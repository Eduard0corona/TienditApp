using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The orders an AI assistant prepares through the MCP server (Fase 32B, ADR 0096): only from a business that takes orders
/// online and did not turn them off, with the products its menu takes priced now, a link that fills the store's cart for a
/// day, and confirmed once the customer sends the order from the store.
/// </summary>
public class AssistantOrdersTests
{
    [Fact]
    public async Task Prepare_ShouldKeep_WhatTheMenuTakes_PricedNow_AndSayWhyNotTheRest()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, grande, concha) = await StoreAsync(fixture);

        var result = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items =
            [
                new AssistantOrderItemInput { ProductId = latte.Id, Quantity = 2, OptionIds = [grande, grande], Notes = " sin azúcar " },
                // Without the size its group needs.
                new AssistantOrderItemInput { ProductId = latte.Id, Quantity = 1 },
                new AssistantOrderItemInput { ProductId = Guid.NewGuid(), Quantity = 1 },
                new AssistantOrderItemInput { ProductId = concha.Id, Quantity = 1 }
            ],
            Source = "Claude-User"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var prepared = result.Value!;
        Assert.True(Domain.Orders.Entities.Order.IsTrackingToken(prepared.Token));
        Assert.Equal(("Negocio cafe-luna", "cafe-luna"), (prepared.BusinessName, prepared.Slug));
        Assert.Equal(
            [
                new PreparedAssistantOrderLineDto(latte.Id, "Latte", ["Grande"], 2, 67m, 134m, "sin azúcar"),
                new PreparedAssistantOrderLineDto(concha.Id, "Concha", [], 1, 18m, 18m, null)
            ],
            prepared.Lines, new LineComparer());
        Assert.Equal([2, 3], prepared.Refused.Select(line => line.Position));
        Assert.Contains("Tamaño", prepared.Refused[0].Reason);
        Assert.Equal(PublicOrderPlacement.NotOnMenuMessage, prepared.Refused[1].Reason);
        // Prices without IVA: the 16 % goes on top, as the store adds it.
        Assert.False(prepared.PricesIncludeTax);
        Assert.Equal(176.32m, prepared.ItemsTotal);
        Assert.True(prepared.TakesOrdersNow);
        Assert.Null(prepared.NotNowReason);
        Assert.Null(prepared.Delivery);
        Assert.Equal(SalesTestFixture.Now.AddHours(24), prepared.ExpiresAt);
        // Said at the business's offset (Centro), so the assistant tells the customer the business's clock, never UTC.
        Assert.Equal(TimeSpan.FromHours(-6), prepared.ExpiresAt.Offset);

        // Kept as the lines of a cart, never a price, with who prepared it; nothing reaches the business.
        var kept = await fixture.Context.AssistantOrders.SingleAsync();
        Assert.Equal(tenant.Id, kept.TenantId);
        Assert.Equal(prepared.Token, kept.Token);
        Assert.Equal(
            [new AccountCartLine(latte.Id, [grande], 2, "sin azúcar"), new AccountCartLine(concha.Id, [], 1, null)],
            kept.Lines);
        Assert.Equal(176.32m, kept.ItemsTotal);
        Assert.Equal("Claude-User", kept.Source);
        Assert.Equal(AssistantOrderStatus.Pending, kept.Status);
        Assert.Empty(await fixture.Context.Orders.ToListAsync());
    }

    [Fact]
    public async Task Prepare_ShouldRefuse_ABusinessThatIsNotThere_TakesNoOrdersOnline_OrTurnedThemOff()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, _, _) = await StoreAsync(fixture);
        var asked = new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items = [new AssistantOrderItemInput { ProductId = latte.Id }]
        };

        Assert.Equal(AssistantOrders.NotFoundMessage,
            (await PrepareHandler(fixture).Handle(asked with { Slug = "no-existe" }, CancellationToken.None)).Error);

        await SettingsAsync(fixture, tenant, settings => settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly));
        Assert.Equal(PrepareAssistantOrderCommandHandler.NoOnlineOrdersMessage,
            (await PrepareHandler(fixture).Handle(asked, CancellationToken.None)).Error);

        await SettingsAsync(fixture, tenant, settings =>
        {
            settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
            settings.SetAssistantOrders(false);
        });
        Assert.Equal(PrepareAssistantOrderCommandHandler.TurnedOffMessage,
            (await PrepareHandler(fixture).Handle(asked, CancellationToken.None)).Error);
        Assert.Empty(await fixture.Context.AssistantOrders.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Prepare_ShouldRefuse_WhenTheMenuTakesNoneOfIt_SayingWhy()
    {
        await using var fixture = SalesTestFixture.Create();
        await StoreAsync(fixture);

        var result = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items = [new AssistantOrderItemInput { ProductId = Guid.NewGuid() }]
        }, CancellationToken.None);

        Assert.Equal("No se pudo agregar ningún producto: " + PublicOrderPlacement.NotOnMenuMessage, result.Error);
        Assert.Empty(await fixture.Context.AssistantOrders.ToListAsync());
    }

    [Fact]
    public async Task Prepare_ShouldStop_AtTheMostABusinessMayHaveWaiting_NotCountingTheExpiredOrSent()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, _, concha) = await StoreAsync(fixture);
        for (var i = 0; i < AssistantOrder.MaxPendingPerBusiness - 1; i++)
            fixture.Context.AssistantOrders.Add(Waiting(tenant, latte, SalesTestFixture.Now.AddHours(-1)));
        // An expired one and a sent one do not count.
        fixture.Context.AssistantOrders.Add(Waiting(tenant, latte, SalesTestFixture.Now.AddHours(-30)));
        var sent = Waiting(tenant, latte, SalesTestFixture.Now.AddHours(-2));
        sent.Confirm(Guid.NewGuid(), SalesTestFixture.Now.AddHours(-1));
        fixture.Context.AssistantOrders.Add(sent);
        await fixture.Context.SaveChangesAsync();
        var asked = new PrepareAssistantOrderCommand { Slug = "cafe-luna", Items = [new AssistantOrderItemInput { ProductId = concha.Id }] };

        Assert.True((await PrepareHandler(fixture).Handle(asked, CancellationToken.None)).IsSuccess);
        Assert.Equal(PrepareAssistantOrderCommandHandler.BusyMessage,
            (await PrepareHandler(fixture).Handle(asked, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Prepare_ShouldPutTheBusinessInLine_BeforeCountingTheOnesWaiting_AndNotOneThatTakesNone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, _, concha) = await StoreAsync(fixture);
        var spy = new SpyLock(fixture);
        var handler = new PrepareAssistantOrderCommandHandler(fixture.Context, fixture.TenantService, spy, fixture.Clock);
        var asked = new PrepareAssistantOrderCommand { Slug = "cafe-luna", Items = [new AssistantOrderItemInput { ProductId = concha.Id }] };

        Assert.True((await handler.Handle(asked, CancellationToken.None)).IsSuccess);
        // In line before it saves: nothing of this request was saved yet.
        Assert.Equal([(tenant.Id, 0)], spy.Acquired);

        // And before it counts: another assistant fills the last places while this one waits in line, and this one sees them.
        spy.FillWhileWaiting = true;
        Assert.Equal(PrepareAssistantOrderCommandHandler.BusyMessage, (await handler.Handle(asked, CancellationToken.None)).Error);
        Assert.Equal(2, spy.Acquired.Count);

        await SettingsAsync(fixture, tenant, settings => settings.SetAssistantOrders(false));
        Assert.False((await handler.Handle(asked, CancellationToken.None)).IsSuccess);
        Assert.Equal(2, spy.Acquired.Count);
    }

    [Fact]
    public async Task Prepare_ShouldSayItTakesNoOrdersNow_AndWhetherItDeliversToThePostalCode()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, _, _, concha) = await StoreAsync(fixture, openDrawer: false);

        var result = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items = [new AssistantOrderItemInput { ProductId = concha.Id }],
            PostalCode = " 72000 "
        }, CancellationToken.None);

        // No drawer open: paused, and the link keeps the order for later.
        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.TakesOrdersNow);
        Assert.Equal(PlacePublicOrderCommandHandler.OrdersPausedMessage, result.Value.NotNowReason);
        // Without delivery zones it delivers nowhere: the store says how to get it.
        Assert.Equal("72000", result.Value.Delivery!.PostalCode);
        Assert.False(result.Value.Delivery.Covered);
        Assert.Equal("72000", (await fixture.Context.AssistantOrders.SingleAsync()).PostalCode);
    }

    [Fact]
    public async Task Prepare_ShouldSayItOnlyDelivers_AlsoWhileItTakesNoOrders()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, _, concha) = await StoreAsync(fixture, openDrawer: false);
        // A kitchen without premises that delivers to 72000, paused (no drawer open).
        var kitchen = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        kitchen.Update(kitchen.Name, kitchen.Code, new BranchContact(AddressLine1: "Privada Los Pinos 8"), BranchServiceMode.DeliveryOnly);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 35m, 0m, null, ["72000"])));
        await fixture.Context.SaveChangesAsync();
        var asked = new PrepareAssistantOrderCommand { Slug = "cafe-luna", Items = [new AssistantOrderItemInput { ProductId = concha.Id }] };

        var paused = await PrepareHandler(fixture).Handle(asked, CancellationToken.None);

        Assert.True(paused.IsSuccess, paused.Error);
        Assert.Equal((false, PlacePublicOrderCommandHandler.OrdersPausedMessage, true),
            (paused.Value!.TakesOrdersNow, paused.Value.NotNowReason, paused.Value.DeliveryOnly));

        // Open, it takes it now, and still only to an address.
        await fixture.OpenCashSessionAsync(tenant);
        var open = await PrepareHandler(fixture).Handle(asked, CancellationToken.None);
        Assert.Equal((true, (string?)null, true), (open.Value!.TakesOrdersNow, open.Value.NotNowReason, open.Value.DeliveryOnly));
    }

    [Fact]
    public async Task Get_ShouldGiveTheCart_WhileItIsOpen_AndSayWhenItExpired_OrIsNotALink()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, grande, _) = await StoreAsync(fixture);
        var token = await PrepareAsync(fixture, latte, grande, "72000");
        await SettingsAsync(fixture, tenant, settings => settings.SetTimeZone("America/Mazatlan"));

        var open = await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token), CancellationToken.None);
        Assert.Equal(AssistantOrderState.Open, open!.State);
        // The same instant, at the offset of the business's zone (Pacífico).
        Assert.Equal((SalesTestFixture.Now.AddHours(24), TimeSpan.FromHours(-7)), (open.ExpiresAt, open.ExpiresAt.Offset));
        Assert.Equal("cafe-luna", open.Slug);
        Assert.Equal([new AssistantOrderLineDto(latte.Id, [grande], 2, null)], open.Lines, new CartLineComparer());
        Assert.Equal("72000", open.PostalCode);
        Assert.Null(open.OrderNumber);

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(24);
        var expired = await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token), CancellationToken.None);
        Assert.Equal(AssistantOrderState.Expired, expired!.State);
        Assert.Empty(expired.Lines);
        Assert.Null(expired.PostalCode);

        // Another link, one that is not one, or the same link in other letters: nothing.
        Assert.Null(await GetHandler(fixture).Handle(new GetAssistantOrderQuery(new string('Z', 43)), CancellationToken.None));
        Assert.Null(await GetHandler(fixture).Handle(new GetAssistantOrderQuery("corto"), CancellationToken.None));
        Assert.Null(await GetHandler(fixture).Handle(new GetAssistantOrderQuery(Swapped(token)), CancellationToken.None));
    }

    [Fact]
    public async Task Get_ShouldFindTheLink_FromAnotherBusinessInContext_ButNotOfASuspendedOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, grande, _) = await StoreAsync(fixture);
        var token = await PrepareAsync(fixture, latte, grande);
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");

        Assert.Equal("cafe-luna", (await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token), CancellationToken.None))!.Slug);

        var business = await fixture.Context.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenant.Id);
        business.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        Assert.Null(await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token), CancellationToken.None));
    }

    [Fact]
    public async Task PlacingTheOrderFromItsLink_ShouldConfirmIt_Once_AndTheAssistantSeesTheOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, latte, grande, _) = await StoreAsync(fixture);
        var token = await PrepareAsync(fixture, latte, grande);

        var placed = await OrderHandler(fixture).Handle(OrderOf(latte, grande) with { AssistantOrderToken = token }, CancellationToken.None);

        Assert.True(placed.IsSuccess, placed.Error);
        var order = await fixture.Context.Orders.SingleAsync();
        var prepared = await fixture.Context.AssistantOrders.SingleAsync();
        Assert.Equal(AssistantOrderStatus.Confirmed, prepared.Status);
        Assert.Equal(order.Id, prepared.OrderId);
        Assert.Equal(SalesTestFixture.Now, prepared.ConfirmedAt);
        Assert.Empty(prepared.Lines);
        var seen = await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token), CancellationToken.None);
        Assert.Equal(AssistantOrderState.Sent, seen!.State);
        Assert.Equal(order.OrderNumber, seen.OrderNumber);
        Assert.Equal("Pendiente", seen.OrderStatus);
        Assert.Empty(seen.Lines);

        // Sent again from the same link: another order of the customer, and the prepared one keeps the first.
        var again = await OrderHandler(fixture).Handle(
            OrderOf(latte, grande) with { AssistantOrderToken = token, CustomerPhone = "5587654321" }, CancellationToken.None);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(order.Id, (await fixture.Context.AssistantOrders.SingleAsync()).OrderId);
    }

    [Fact]
    public async Task PlacingTheOrder_WithALinkExpired_NotOne_OrOfAnotherBusiness_ShouldPlaceIt_AndChangeNothingElse()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, latte, grande, _) = await StoreAsync(fixture);
        var token = await PrepareAsync(fixture, latte, grande);
        // The open link of another business, sent with an order of this one: it stays open, there.
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "panaderia-sol");
        await SettingsAsync(fixture, other, settings => settings.UpdateOnlineMenu(OnlineMenuMode.Orders));
        var bread = await fixture.AddProductAsync(other, "Bolillo", 4m);
        var ofOther = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "panaderia-sol",
            Items = [new AssistantOrderItemInput { ProductId = bread.Id, Quantity = 6 }]
        }, CancellationToken.None);
        Assert.True(ofOther.IsSuccess, ofOther.Error);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);

        var fromOther = await OrderHandler(fixture).Handle(
            OrderOf(latte, grande) with { AssistantOrderToken = ofOther.Value!.Token, CustomerPhone = "5511112222" }, CancellationToken.None);
        Assert.True(fromOther.IsSuccess, fromOther.Error);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(25);
        foreach (var link in new[] { token, "no-es-un-enlace", new string('Q', 43) })
        {
            var placed = await OrderHandler(fixture).Handle(
                OrderOf(latte, grande) with { AssistantOrderToken = link, CustomerPhone = $"55{Random.Shared.Next(10000000, 99999999)}" },
                CancellationToken.None);
            Assert.True(placed.IsSuccess, placed.Error);
        }

        Assert.Equal(4, await fixture.Context.Orders.IgnoreQueryFilters().CountAsync());
        var prepared = await fixture.Context.AssistantOrders.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, prepared.Count);
        Assert.All(prepared, order => Assert.Equal((AssistantOrderStatus.Pending, (Guid?)null), (order.Status, order.OrderId)));
    }

    [Fact]
    public async Task Get_ShouldNotFindTheLink_FromThePageOfAnotherStore()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, latte, grande, _) = await StoreAsync(fixture);
        var token = await PrepareAsync(fixture, latte, grande);
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");

        Assert.Null(await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token, "otro"), CancellationToken.None));
        Assert.Equal(AssistantOrderState.Open,
            (await GetHandler(fixture).Handle(new GetAssistantOrderQuery(token, " Cafe-Luna "), CancellationToken.None))!.State);
    }

    [Fact]
    public async Task Prepare_ShouldJoinTheSameProduct_AsItsLinkFillsTheCart_AndNameEachRefusalByWhereItWasAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, _, _, concha) = await StoreAsync(fixture);
        var unknown = Guid.NewGuid();

        var result = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items =
            [
                new AssistantOrderItemInput { ProductId = concha.Id, Quantity = 60 },
                new AssistantOrderItemInput { ProductId = concha.Id, Quantity = 60, Notes = "  " },
                new AssistantOrderItemInput { ProductId = unknown, Quantity = 1 },
                new AssistantOrderItemInput { ProductId = unknown, Quantity = 2 }
            ]
        }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        // One line of 99, as the cart of its link: what the assistant is told is what the customer sees.
        var line = Assert.Single(result.Value!.Lines);
        Assert.Equal((concha.Id, 99, 99 * 18m), (line.ProductId, line.Quantity, line.Amount));
        var refused = Assert.Single(result.Value.Refused);
        // The third product asked, although it is the second line.
        Assert.Equal((3, unknown), (refused.Position, refused.ProductId));
        var kept = await fixture.Context.AssistantOrders.SingleAsync();
        Assert.Equal(new AccountCartLine(concha.Id, [], 99, null), Assert.Single(kept.Lines));
        Assert.Equal(result.Value.ItemsTotal, kept.ItemsTotal);
    }

    [Fact]
    public void Validators_ShouldRefuse_ALinkLongerThanOneOfItsTokens_WhenTheOrderIsPlacedOrPaidOnline()
    {
        var item = new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 };
        var token = new string('a', Order.TrackingTokenLength);
        var order = new PlacePublicOrderCommand { Slug = "cafe-luna", CustomerName = "Ana", CustomerPhone = "5512345678", Items = [item] };
        var paid = new FoodParkDigital.Application.OnlinePayments.CreateOnlineCheckoutCommand
        {
            Slug = "cafe-luna", CustomerName = "Ana", CustomerPhone = "5512345678", Items = [item]
        };

        Assert.True(new PlacePublicOrderCommandValidator().Validate(order with { AssistantOrderToken = token }).IsValid);
        Assert.False(new PlacePublicOrderCommandValidator().Validate(order with { AssistantOrderToken = token + "a" }).IsValid);
        var checkout = new FoodParkDigital.Application.OnlinePayments.CreateOnlineCheckoutCommandValidator();
        Assert.True(checkout.Validate(paid with { AssistantOrderToken = token }).IsValid);
        Assert.False(checkout.Validate(paid with { AssistantOrderToken = token + "a" }).IsValid);
    }

    [Fact]
    public async Task TheBusiness_ShouldTurnOrdersFromAssistantsOffAndOn_WithItsRecord_AndKeepItWhenItDoesNotSay()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, _, _, _) = await StoreAsync(fixture);
        var handler = new UpdateOnlineMenuCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser);

        var off = await handler.Handle(new UpdateOnlineMenuCommand(OnlineMenuMode.Orders, AssistantOrders: false), CancellationToken.None);
        Assert.False(off.Value!.AssistantOrders);
        Assert.False((await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(x => x.TenantId == tenant.Id)).AssistantOrders);
        var record = Assert.Single(await fixture.Context.AuditLogs.IgnoreQueryFilters()
            .Where(log => log.Action == "AssistantOrdersUpdated").ToListAsync());
        Assert.Contains("\"Enabled\":false", record.Metadata);

        // Changing only the mode, it stays as it is; turned on again, a second record.
        var kept = await handler.Handle(new UpdateOnlineMenuCommand(OnlineMenuMode.MenuOnly), CancellationToken.None);
        Assert.False(kept.Value!.AssistantOrders);
        var on = await handler.Handle(new UpdateOnlineMenuCommand(OnlineMenuMode.Orders, AssistantOrders: true), CancellationToken.None);
        Assert.True(on.Value!.AssistantOrders);
        Assert.Equal(2, await fixture.Context.AuditLogs.IgnoreQueryFilters().CountAsync(log => log.Action == "AssistantOrdersUpdated"));
    }

    [Fact]
    public async Task TheCleanup_ShouldDelete_TheOrdersExpiredAWeekAgo_SentOrNot_OfEveryBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, latte, _, _) = await StoreAsync(fixture);
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var old = Waiting(tenant, latte, SalesTestFixture.Now.AddDays(-9));
        var oldSent = Waiting(other, latte, SalesTestFixture.Now.AddDays(-9));
        oldSent.Confirm(Guid.NewGuid(), SalesTestFixture.Now.AddDays(-9).AddHours(1));
        // Expired six days ago, and still open: they stay.
        var recent = Waiting(tenant, latte, SalesTestFixture.Now.AddDays(-7));
        var open = Waiting(other, latte, SalesTestFixture.Now.AddHours(-1));
        fixture.Context.AssistantOrders.AddRange(old, oldSent, recent, open);
        await fixture.Context.SaveChangesAsync();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            // An instance the provider does not dispose: the fixture owns the context.
            .AddSingleton<Common.Interfaces.IApplicationDbContext>(fixture.Context)
            .BuildServiceProvider();
        var cleanup = new Infrastructure.BackgroundJobs.AssistantOrders.AssistantOrdersCleanupService(
            services.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            fixture.Clock,
            NullLogger<Infrastructure.BackgroundJobs.AssistantOrders.AssistantOrdersCleanupService>.Instance);

        Assert.Equal(2, await cleanup.DeleteExpiredAsync(CancellationToken.None));
        Assert.Equal(new[] { recent.Id, open.Id }.Order(),
            (await fixture.Context.AssistantOrders.IgnoreQueryFilters().Select(o => o.Id).ToListAsync()).Order());
        Assert.Equal(0, await cleanup.DeleteExpiredAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0, null, "La cantidad de un producto va de 1 a 99.")]
    [InlineData(100, null, "La cantidad de un producto va de 1 a 99.")]
    [InlineData(1, "7200", "El código postal lleva 5 dígitos.")]
    public void Validator_ShouldRefuse_AQuantityOutOfItsLimits_OrABadPostalCode(int quantity, string? postalCode, string message)
    {
        var result = new PrepareAssistantOrderCommandValidator().Validate(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items = [new AssistantOrderItemInput { ProductId = Guid.NewGuid(), Quantity = quantity }],
            PostalCode = postalCode
        });

        Assert.Equal(message, Assert.Single(result.Errors).ErrorMessage);
    }

    [Fact]
    public void Validator_ShouldRefuse_NoProducts_MoreThanACartTakes_AndAnEmptyOptionId()
    {
        var validator = new PrepareAssistantOrderCommandValidator();
        Assert.Contains(validator.Validate(new PrepareAssistantOrderCommand { Slug = "cafe-luna" }).Errors,
            error => error.ErrorMessage == "Agrega al menos un producto.");
        Assert.Contains(validator.Validate(new PrepareAssistantOrderCommand
            {
                Slug = "cafe-luna",
                Items = Enumerable.Range(0, AccountCart.MaxLines + 1).Select(_ => new AssistantOrderItemInput { ProductId = Guid.NewGuid() }).ToList()
            }).Errors,
            error => error.ErrorMessage == "Un pedido lleva hasta 50 productos distintos.");
        Assert.Contains(validator.Validate(new PrepareAssistantOrderCommand
            {
                Slug = "cafe-luna",
                Items = [new AssistantOrderItemInput { ProductId = Guid.NewGuid(), OptionIds = [Guid.Empty] }]
            }).Errors,
            error => error.ErrorMessage == "Una opción no tiene un id válido.");
    }

    [Fact]
    public async Task Reorder_ShouldPrepare_WhatTheMenuTakesToday_OfAnOrderOfTheAccount_AndSayWhatNot()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, latte, grande, concha) = await StoreAsync(fixture);
        var account = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "cliente@demo.com", Email = "cliente@demo.com", FirstName = "Ana", LastName = "López",
            Role = UserRole.Customer, IsActive = true
        };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = "CUSTOMER";
        var placed = await OrderHandler(fixture).Handle(OrderOf(latte, grande) with
        {
            Items =
            [
                new CreateOrderItemDto { ProductId = latte.Id, Quantity = 2, OptionIds = [grande], Notes = "sin azúcar" },
                new CreateOrderItemDto { ProductId = concha.Id, Quantity = 1 }
            ]
        }, CancellationToken.None);
        Assert.True(placed.IsSuccess, placed.Error);
        var orderId = (await fixture.Context.Orders.IgnoreQueryFilters().SingleAsync(o => o.CustomerUserId == account.Id)).Id;
        // The concha left the menu since; the latte costs more today (55 then, 60 now, and 12 for the grande).
        concha.SetAvailability(false);
        Reprice(latte, 60m);
        await fixture.Context.SaveChangesAsync();

        var result = await ReorderHandler(fixture).Handle(
            new PrepareAssistantReorderCommand { OrderId = orderId, Source = "Claude" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var line = Assert.Single(result.Value!.Order.Lines);
        Assert.Equal((latte.Id, 2, "sin azúcar", "Grande"), (line.ProductId, line.Quantity, line.Notes, Assert.Single(line.Options)));
        // At today's price, not at the order's.
        Assert.Equal((72m, 144m), (line.UnitPrice, line.Amount));
        Assert.StartsWith("Concha: ", Assert.Single(result.Value.Left));
        var kept = await fixture.Context.AssistantOrders.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((result.Value.Order.Token, "Claude"), (kept.Token, kept.Source));

        // Another account's order is not found, nor is one that does not exist.
        fixture.CurrentUser.UserId = Guid.NewGuid().ToString();
        Assert.Equal(PrepareAssistantReorderCommandHandler.NotFoundMessage, (await ReorderHandler(fixture).Handle(
            new PrepareAssistantReorderCommand { OrderId = orderId }, CancellationToken.None)).Error);
        Assert.Equal(1, await fixture.Context.AssistantOrders.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Reorder_ShouldSay_WhatGoesWithLess_MoreThan99OfOne_AndANoteLongerThanACartKeeps()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, latte, grande, concha) = await StoreAsync(fixture);
        var account = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = "cliente@demo.com", Email = "cliente@demo.com", FirstName = "Ana", LastName = "López",
            Role = UserRole.Customer, IsActive = true
        };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = "CUSTOMER";
        // 120 conchas in two lines of 60, and a latte whose note (written at the counter) is longer than a cart keeps.
        var placed = await OrderHandler(fixture).Handle(OrderOf(latte, grande) with
        {
            Items =
            [
                new CreateOrderItemDto { ProductId = concha.Id, Quantity = 60 },
                new CreateOrderItemDto { ProductId = concha.Id, Quantity = 60 },
                new CreateOrderItemDto { ProductId = latte.Id, Quantity = 1, OptionIds = [grande], Notes = "nota" }
            ]
        }, CancellationToken.None);
        Assert.True(placed.IsSuccess, placed.Error);
        var order = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.Items).SingleAsync(o => o.CustomerUserId == account.Id);
        fixture.Context.Entry(order.Items.Single(i => i.ProductId == latte.Id)).Property("Notes").CurrentValue = new string('n', 320);
        await fixture.Context.SaveChangesAsync();

        var result = await ReorderHandler(fixture).Handle(new PrepareAssistantReorderCommand { OrderId = order.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(99, result.Value!.Order.Lines.Single(l => l.ProductId == concha.Id).Quantity);
        Assert.Equal(300, result.Value.Order.Lines.Single(l => l.ProductId == latte.Id).Notes!.Length);
        Assert.Equal(
            ["Latte: su nota se recortó a 300 caracteres.", "Concha: un pedido lleva hasta 99; van 99 de 120."],
            result.Value.Left);
    }

    private static void Reprice(Product product, decimal price)
        => product.Update(
            product.CategoryId, product.Name, product.Description, price, product.PriceCurrency, product.PreparationTimeMinutes,
            product.Sku, product.Barcode, product.SortOrder, product.IsFeatured, product.Tags, product.CompareAtPrice, product.TaxRate,
            product.Calories, product.Allergens, product.MaxQuantityPerOrder, product.Unit, product.AvailableFromTime,
            product.AvailableToTime, product.IsTrackable, product.NotifyCustomer, product.IsTaxExempt);

    private static async Task<(Tenant Tenant, Product Latte, Guid Grande, Product Concha)> StoreAsync(
        SalesTestFixture fixture, bool openDrawer = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");
        await SettingsAsync(fixture, tenant, settings => settings.UpdateOnlineMenu(OnlineMenuMode.Orders));
        if (openDrawer)
            await fixture.OpenCashSessionAsync(tenant);
        var latte = await fixture.AddProductAsync(tenant, "Latte", 55m);
        latte.SetOptionGroups([new ProductOptionGroupDefinition(null, "Tamaño", 1, 1, [new ProductOptionDefinition(null, "Grande", 12m)])]);
        var concha = await fixture.AddProductAsync(tenant, "Concha", 18m);
        await fixture.Context.SaveChangesAsync();
        return (tenant, latte, latte.OptionGroups.Single().Options.Single().Id, concha);
    }

    private static async Task SettingsAsync(SalesTestFixture fixture, Tenant tenant, Action<TenantSettings> change)
    {
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        change(settings);
        await fixture.Context.SaveChangesAsync();
    }

    private static async Task<string> PrepareAsync(SalesTestFixture fixture, Product latte, Guid grande, string? postalCode = null)
    {
        var result = await PrepareHandler(fixture).Handle(new PrepareAssistantOrderCommand
        {
            Slug = "cafe-luna",
            Items = [new AssistantOrderItemInput { ProductId = latte.Id, Quantity = 2, OptionIds = [grande] }],
            PostalCode = postalCode
        }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Token;
    }

    private static AssistantOrder Waiting(Tenant tenant, Product latte, DateTimeOffset preparedAt)
        => AssistantOrder.Prepare(tenant.Id, OrderTracking.NewToken(), [new AccountCartLine(latte.Id, [], 1, null)],
            null, 55m, null, preparedAt);

    /// <summary>The same characters with the case of the first letter changed: the column compares without case.</summary>
    private static string Swapped(string token)
    {
        var index = token.IndexOfAny("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray());
        var letter = token[index];
        return token[..index] + (char.IsUpper(letter) ? char.ToLowerInvariant(letter) : char.ToUpperInvariant(letter)) + token[(index + 1)..];
    }

    private static PlacePublicOrderCommand OrderOf(Product latte, Guid grande) => new()
    {
        Slug = "cafe-luna",
        CustomerName = "Ana",
        CustomerPhone = "5512345678",
        Items = [new CreateOrderItemDto { ProductId = latte.Id, Quantity = 2, OptionIds = [grande] }]
    };

    private static PrepareAssistantOrderCommandHandler PrepareHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, new AssistantOrdersLock(fixture.Context), fixture.Clock);

    private static PrepareAssistantReorderCommandHandler ReorderHandler(SalesTestFixture fixture)
        => new(new Dispatcher(fixture));

    /// <summary>Sends what «pedir otra vez» asks to the handlers it reaches, as the pipeline would.</summary>
    private sealed class Dispatcher(SalesTestFixture fixture) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => request switch
            {
                GetMyOrderQuery query => (TResponse)(object)(await new GetMyOrderQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
                    .Handle(query, cancellationToken))!,
                PrepareAssistantOrderCommand command => (TResponse)(object)await PrepareHandler(fixture).Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static GetAssistantOrderQueryHandler GetHandler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.Clock);

    private static PlacePublicOrderCommandHandler OrderHandler(SalesTestFixture fixture)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        return new PlacePublicOrderCommandHandler(
            fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
            new FolioGenerator(fixture.Context), fixture.CurrentUser);
    }

    /// <summary>
    /// Records each business put in line and how many orders it had prepared then; with <see cref="FillWhileWaiting"/>,
    /// another assistant takes the places left while this one waits.
    /// </summary>
    private sealed class SpyLock(SalesTestFixture fixture) : IAssistantOrdersLock
    {
        public List<(Guid TenantId, int Prepared)> Acquired { get; } = [];
        public bool FillWhileWaiting { get; set; }

        public async Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var prepared = await fixture.Context.AssistantOrders.IgnoreQueryFilters().CountAsync(cancellationToken);
            Acquired.Add((tenantId, prepared));
            if (!FillWhileWaiting)
                return;
            for (var i = prepared; i < AssistantOrder.MaxPendingPerBusiness; i++)
                fixture.Context.AssistantOrders.Add(AssistantOrder.Prepare(tenantId, OrderTracking.NewToken(),
                    [new AccountCartLine(Guid.NewGuid(), [], 1, null)], null, 18m, null, fixture.Clock.UtcNow));
            await fixture.Context.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class LineComparer : IEqualityComparer<PreparedAssistantOrderLineDto>
    {
        public bool Equals(PreparedAssistantOrderLineDto? x, PreparedAssistantOrderLineDto? y)
            => x is not null && y is not null && x.ProductId == y.ProductId && x.Name == y.Name && x.Options.SequenceEqual(y.Options)
                && x.Quantity == y.Quantity && x.UnitPrice == y.UnitPrice && x.Amount == y.Amount && x.Notes == y.Notes;

        public int GetHashCode(PreparedAssistantOrderLineDto line) => line.ProductId.GetHashCode();
    }

    private sealed class CartLineComparer : IEqualityComparer<AssistantOrderLineDto>
    {
        public bool Equals(AssistantOrderLineDto? x, AssistantOrderLineDto? y)
            => x is not null && y is not null && x.ProductId == y.ProductId && x.OptionIds.SequenceEqual(y.OptionIds)
                && x.Quantity == y.Quantity && x.Notes == y.Notes;

        public int GetHashCode(AssistantOrderLineDto line) => line.ProductId.GetHashCode();
    }
}
