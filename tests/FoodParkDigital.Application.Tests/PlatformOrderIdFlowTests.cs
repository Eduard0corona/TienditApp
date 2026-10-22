using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The platform's long id of an order (Fase 22I, ADR 0041): written in Vender or later, at any status; the same one on
/// another order that counts is a warning the business confirms; Pedidos and Reportes find the order by it; and the
/// activity keeps every change.
/// </summary>
public class PlatformOrderIdFlowTests
{
    private const string UberUuid = "3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b84";
    private const string UberUuidStored = "3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B84";
    private static readonly DateOnly Today = new(2026, 10, 4);

    [Fact]
    public async Task OrderWithALongId_ShouldKeepItNormalized_AndShowItInTheDetail()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);

        var sale = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id) with
        {
            ExternalOrderId = $" #{UberUuid} "
        });
        var without = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC954", pack.Id) with
        {
            ExternalOrderId = "   "
        });

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Equal(("BC953", UberUuidStored), (sale.Value.PlatformOrder!.ExternalOrderNumber, sale.Value.PlatformOrder.ExternalOrderId));
        Assert.True(without.IsSuccess, without.Error);
        Assert.Null(without.Value.PlatformOrder!.ExternalOrderId);
        var detail = await new GetOrderByIdQueryHandler(fixture.Context).Handle(new GetOrderByIdQuery(sale.Value.Id), CancellationToken.None);
        Assert.Equal(UberUuidStored, detail.PlatformOrder!.ExternalOrderId);
    }

    [Fact]
    public void Validator_ShouldRefuseALongIdThatIsNotOne_OrOnAnOrderThatIsNotOfAPlatform()
    {
        var validator = new CreateOrderCommandValidator();
        var platform = SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "4821", Guid.NewGuid());

        var shortOne = validator.Validate(platform with { ExternalOrderId = "4821" });
        var tooLong = validator.Validate(platform with { ExternalOrderId = new string('9', PlatformOrderId.MaxLength + 1) });
        var counter = validator.Validate(new CreateOrderCommand
        {
            ExternalOrderId = "1029384756102938475",
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        });

        Assert.Equal(PlatformOrderId.InvalidMessage, Assert.Single(shortOne.Errors).ErrorMessage);
        Assert.Equal(PlatformOrderId.InvalidMessage, Assert.Single(tooLong.Errors).ErrorMessage);
        Assert.Equal(CreateOrderCommandValidator.PlatformFieldsOnlyForPlatformsMessage, Assert.Single(counter.Errors).ErrorMessage);
        Assert.True(validator.Validate(platform with { ExternalOrderId = "1029384756 102938475" }).IsValid);
    }

    [Fact]
    public async Task SameLongIdOfThePlatform_OnAnyDay_ShouldBeAskedFirst_AndRegisteredOnceConfirmed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);
        var key = Guid.NewGuid();

        var first = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", pack.Id) with
        {
            ExternalOrderId = "1029384756102938475"
        });
        // A week later, with another pickup number: the long id does not repeat, so it is the same order again.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(7);
        var again = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "31", pack.Id) with
        {
            ExternalOrderId = "1029384756 102938475",
            IdempotencyKey = key
        });
        // The same long id in another platform is another order.
        var rappi = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.Rappi, "31", pack.Id) with
        {
            ExternalOrderId = "1029384756102938475"
        });
        var confirmed = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "31", pack.Id) with
        {
            ExternalOrderId = "1029384756102938475",
            IdempotencyKey = key,
            ConfirmPossibleDuplicate = true
        });

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(PlatformOrderErrors.PossibleDuplicateLongId, again.Error);
        Assert.True(rappi.IsSuccess, rappi.Error);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(2, await fixture.Context.PlatformOrderDetails.CountAsync(d => d.ExternalOrderId == "1029384756102938475" && d.Channel == OrderChannel.DidiFood));
        Assert.True(PlatformOrderErrors.IsPossibleDuplicate(again.Error));
    }

    [Fact]
    public async Task SameLongId_ShouldPass_WhenTheOtherOrderWasCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        var cancelled = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id, collectedBy: PlatformCollectedBy.Business) with
        {
            ExternalOrderId = UberUuid
        });
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == cancelled.Value.Id);
        order.Cancel(new FoodOrderStateMachine(), "cajero", "Capturado dos veces");
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var again = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id) with
        {
            ExternalOrderId = UberUuid
        });

        // The cancelled order does not count: neither its folio nor its long id ask again.
        Assert.True(again.IsSuccess, again.Error);
    }

    [Fact]
    public async Task LongId_ShouldBeWrittenCorrectedAndCleared_AtAnyStatus_KeepingEachChangeInTheActivity()
    {
        var mediator = new RecordingMediator();
        await using var fixture = SalesTestFixture.Create(mediator);
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        var sale = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id));
        // Without a kitchen screen the order was handed over: it is delivered.
        Assert.Equal(OrderStatus.Delivered, sale.Value.Status);

        var written = await SetAsync(fixture, sale.Value.Id, UberUuid);
        var same = await SetAsync(fixture, sale.Value.Id, $"#{UberUuidStored}");
        var corrected = await SetAsync(fixture, sale.Value.Id, "3f9c2b1e-8d4a-4c6f-9b2e-1a7d5e0c3b85");
        var cleared = await SetAsync(fixture, sale.Value.Id, "  ");

        Assert.True(written.IsSuccess, written.Error);
        Assert.True(same.IsSuccess, same.Error);
        Assert.True(corrected.IsSuccess, corrected.Error);
        Assert.True(cleared.IsSuccess, cleared.Error);
        Assert.Null((await fixture.Context.PlatformOrderDetails.SingleAsync()).ExternalOrderId);
        // Writing the same one again changed nothing: three changes, each with the one before.
        var changes = mediator.Published.OfType<OrderPlatformIdChangedEvent>().ToList();
        Assert.Equal(
            [(null, UberUuidStored), (UberUuidStored, "3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B85"), ("3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B85", null)],
            changes.Select(c => (c.PreviousExternalOrderId, c.ExternalOrderId)).ToList());
        await new OrderAuditHandlers(fixture.Context).HandleAsync(changes[1], OutboxContext(tenant.Id), CancellationToken.None);
        await fixture.Context.SaveChangesAsync();
        var log = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "OrderPlatformIdChanged");
        var metadata = JsonDocument.Parse(log.Metadata!).RootElement;
        Assert.Equal(sale.Value.Id.ToString(), log.EntityId);
        Assert.Equal("BC953", metadata.GetProperty("ExternalOrderNumber").GetString());
        Assert.Equal(UberUuidStored, metadata.GetProperty("PreviousExternalOrderId").GetString());
        Assert.Equal("3F9C2B1E-8D4A-4C6F-9B2E-1A7D5E0C3B85", metadata.GetProperty("ExternalOrderId").GetString());
    }

    [Fact]
    public async Task WritingALongIdAnotherOrderHas_ShouldBeAskedFirst_AndSavedOnceConfirmed()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.UberEats);
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "BC953", pack.Id) with { ExternalOrderId = UberUuid });
        var other = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.UberEats, "XA101", pack.Id));

        var asked = await SetAsync(fixture, other.Value.Id, UberUuid);
        var confirmed = await SetAsync(fixture, other.Value.Id, UberUuid, confirm: true);

        Assert.Equal(PlatformOrderErrors.LongIdInUse, asked.Error);
        Assert.True(PlatformOrderErrors.IsPossibleDuplicate(asked.Error));
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(2, await fixture.Context.PlatformOrderDetails.CountAsync(d => d.ExternalOrderId == UberUuidStored));
    }

    [Fact]
    public async Task LongId_ShouldBeRefused_OnAnOrderThatIsNotOfAPlatform_OrOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, pack) = await ShopAsync(fixture);
        var counter = await SellAsync(fixture, new CreateOrderCommand
        {
            CustomerName = "Mostrador",
            Items = [new CreateOrderItemDto { ProductId = pack.Id, Quantity = 1 }]
        });
        Assert.True(counter.IsSuccess, counter.Error);

        var notAPlatform = await SetAsync(fixture, counter.Value.Id, UberUuid);
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "otro");

        Assert.Equal(PlatformOrderRules.OnlyPlatformOrdersHaveALongIdMessage, notAPlatform.Error);
        // From the other business the order does not exist.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => SetAsync(fixture, counter.Value.Id, UberUuid));
    }

    [Theory]
    [InlineData("4821")]
    [InlineData("3f9c_2b1e-8d4a")]
    public void SetValidator_ShouldRefuseWhatIsNotALongId_ButAcceptAnEmptyBox(string raw)
    {
        var validator = new SetPlatformOrderIdCommandValidator();

        Assert.Equal(PlatformOrderId.InvalidMessage, Assert.Single(validator.Validate(new SetPlatformOrderIdCommand(Guid.NewGuid(), raw)).Errors).ErrorMessage);
        Assert.True(validator.Validate(new SetPlatformOrderIdCommand(Guid.NewGuid(), null)).IsValid);
        Assert.True(validator.Validate(new SetPlatformOrderIdCommand(Guid.NewGuid(), "")).IsValid);
    }

    [Fact]
    public async Task OrdersAndReports_ShouldFindTheOrderByAPartOfItsLongId()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, pack) = await ShopAsync(fixture);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        var sale = await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "12", pack.Id) with
        {
            ExternalOrderId = "1029384756102938475"
        });
        await SellAsync(fixture, SalesTestFixture.PlatformOrder(OrderChannel.DidiFood, "13", pack.Id));

        var orders = await new GetOrdersByBusinessQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetOrdersByBusinessQuery { Search = "2938 475" }, CancellationToken.None);
        var report = await new GetDeliveryPlatformOrdersQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetDeliveryPlatformOrdersQuery { Channel = OrderChannel.DidiFood, From = Today, To = Today, Search = "#102938475" }, CancellationToken.None);

        Assert.Equal(sale.Value.Id, Assert.Single(orders.Items).Id);
        Assert.Equal("1029384756102938475", orders.Items[0].PlatformOrder!.ExternalOrderId);
        var row = Assert.Single(report.Items);
        Assert.Equal((sale.Value.Id, "12", "1029384756102938475"), (row.OrderId, row.ExternalOrderNumber, row.ExternalOrderId));
    }

    /// <summary>A kitchen selling with IVA included: a $1,000 family pack.</summary>
    private static async Task<(Tenant Tenant, Product Pack)> ShopAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var pack = await fixture.AddProductAsync(tenant, "Paquete familiar", 1000m);
        return (tenant, pack);
    }

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<OrderDto>> SellAsync(SalesTestFixture fixture, CreateOrderCommand command)
    {
        var result = await fixture.CreateOrderHandler().Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> SetAsync(SalesTestFixture fixture, Guid orderId, string? longId, bool confirm = false)
    {
        try
        {
            return await new SetPlatformOrderIdCommandHandler(fixture.Context, fixture.TenantService)
                .Handle(new SetPlatformOrderIdCommand(orderId, longId, confirm), CancellationToken.None);
        }
        finally
        {
            fixture.Context.ChangeTracker.Clear();
        }
    }

    private static OutboxEventContext OutboxContext(Guid tenantId)
        => new(Guid.NewGuid(), tenantId, "user-1", "cajero", SalesTestFixture.Now, 1);

    private sealed class RecordingMediator : IMediator
    {
        public List<DomainEvent> Published { get; } = [];

        public Task Publish(object notification, CancellationToken ct = default)
        {
            if (notification is DomainEvent domainEvent)
                Published.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification
            => Publish((object)notification!, ct);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
