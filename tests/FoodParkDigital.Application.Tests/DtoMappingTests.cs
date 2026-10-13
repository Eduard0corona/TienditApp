using System.Text.Json;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The DTOs are mapped by hand (AutoMapper was removed for GHSA-rvv3-g6hj-g44x). Lists project in SQL with an
/// expression and single entities map in memory: both must return the same, member by member, for entities with every
/// member set, and keep the rules the API relies on.
/// </summary>
public class DtoMappingTests
{
    public static TheoryData<int> Seeds => [1, 2, 3];

    [Theory, MemberData(nameof(Seeds))]
    public void Order_ShouldMapTheSame_InMemoryAndForSql(int seed)
    {
        var order = new EntityFiller(maxDepth: 3, seed).Create<Order>();
        Assert.NotNull(order.Delivery);

        Same(order.ToDto(), Project(order, OrderMappings.AsDto));
        EntityFiller.Set(order, nameof(Order.ServiceDetail), null);
        Same(order.ToDto(), Project(order, OrderMappings.AsDto));
        // An order that does not go to an address (Fase 22C).
        EntityFiller.Set(order, nameof(Order.Delivery), null);
        Same(order.ToDto(), Project(order, OrderMappings.AsDto));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void Product_ShouldMapTheSame_InMemoryAndForSql(int seed)
    {
        var product = new EntityFiller(maxDepth: 3, seed).Create<Product>();

        Same(product.ToDto(), Project(product, CatalogMappings.AsDto));
        EntityFiller.Set(product, nameof(Product.ImageUrl), null);
        EntityFiller.Set(product, nameof(Product.CompareAtPrice), null);
        Same(product.ToDto(), Project(product, CatalogMappings.AsDto));
    }

    [Theory, MemberData(nameof(Seeds))]
    public void CompiledMappings_ShouldMatchTheirExpression(int seed)
    {
        var filler = new EntityFiller(maxDepth: 2, seed);
        var location = filler.Create<InventoryLocation>();
        var agent = filler.Create<AiAgent>();
        var conversation = filler.Create<WhatsAppConversation>();
        var message = filler.Create<WhatsAppMessage>();

        Same(location.ToDto(), Project(location, InventoryMappings.AsDto));
        Same(agent.ToDto(), Project(agent, AiAgentMappings.AsDto));
        Same(conversation.ToDto(), Project(conversation, WhatsAppMappings.ConversationAsDto));
        Same(message.ToDto(), Project(message, WhatsAppMappings.MessageAsDto));
        Assert.Equal(message.Direction.ToString(), message.ToDto().Direction);
    }

    [Fact]
    public void Order_ShouldKeepTheRulesOfTheApi()
    {
        var order = new EntityFiller(maxDepth: 3).Create<Order>();
        EntityFiller.Set(order, nameof(Order.IsTab), true);
        EntityFiller.Set(order, nameof(Order.Status), OrderStatus.Confirmed);
        var payment = order.Payments.First();
        EntityFiller.Set(payment, nameof(OrderPayment.CashReceived), payment.Amount + payment.Tip + 20m);

        var dto = order.ToDto();
        var listed = Project(order, OrderMappings.AsDto);

        Assert.True(dto.IsOpenTab);
        Assert.Equal(order.Total - order.PaidAmount, listed.Balance);
        Assert.Equal(order.Payments.Sum(p => p.Tip), listed.TipTotal);
        Assert.Equal(20m, listed.Payments[0].Change);
        Assert.Equal(dto.KitchenTickets.OrderBy(t => t.Round).Select(t => t.Id), dto.KitchenTickets.Select(t => t.Id));
        // Filled afterwards by ApplyStateMetadata: the mapping leaves them empty.
        Assert.Empty(dto.TaxBreakdown);
        Assert.Empty(dto.AllowedTransitions);
        EntityFiller.Set(order, nameof(Order.Status), OrderStatus.Delivered);
        Assert.False(Project(order, OrderMappings.AsDto).IsOpenTab);
    }

    [Theory, MemberData(nameof(Seeds))]
    public void OrderOfAPlatform_ShouldShowItsFolioAndWhoCharged_InMemoryAndForSql_NeverItsCommission(int seed)
    {
        var order = new EntityFiller(maxDepth: 3, seed).Create<Order>();
        EntityFiller.Set(order, nameof(Order.Channel), OrderChannel.Rappi);
        var detail = order.PlatformDetail!;

        var dto = order.ToDto();
        Same(dto, Project(order, OrderMappings.AsDto));
        Assert.Equal(detail.ExternalOrderNumber, dto.PlatformOrder!.ExternalOrderNumber);
        Assert.Equal(detail.CollectedBy, dto.PlatformOrder.CollectedBy);
        // The commission, the withholdings and the expected settlement are for the reports, not for every screen.
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("Commission", json);
        Assert.DoesNotContain("Withheld", json);
        Assert.DoesNotContain("ExpectedSettlement", json);

        // Any other order has no platform.
        EntityFiller.Set(order, nameof(Order.PlatformDetail), null);
        Same(order.ToDto(), Project(order, OrderMappings.AsDto));
        Assert.Null(order.ToDto().PlatformOrder);
    }

    [Fact]
    public void KitchenOrder_ShouldCarryTheChannelAndTheFolioOfTheApp()
    {
        var order = new EntityFiller(maxDepth: 3).Create<Order>();
        EntityFiller.Set(order, nameof(Order.Channel), OrderChannel.UberEats);
        var folio = order.PlatformDetail!.ExternalOrderNumber;

        var platform = order.ToKitchenDto();
        EntityFiller.Set(order, nameof(Order.Channel), OrderChannel.Counter);
        EntityFiller.Set(order, nameof(Order.PlatformDetail), null);
        var counter = order.ToKitchenDto();

        Assert.Equal((order.Id, OrderChannel.UberEats, folio), (platform.Id, platform.Channel, platform.ExternalOrderNumber));
        Assert.Equal(OrderChannel.Counter, counter.Channel);
        Assert.Null(counter.ExternalOrderNumber);
        // The kitchen knows an order goes out to an address, never where (Fase 22C).
        Assert.True(counter.IsDelivery);
        EntityFiller.Set(order, nameof(Order.Delivery), null);
        Assert.False(order.ToKitchenDto().IsDelivery);
        Assert.DoesNotContain("AddressLine", System.Text.Json.JsonSerializer.Serialize(counter));
    }

    [Fact]
    public void Product_ShouldPointToTheMediumImage_AndComputeItsDiscount()
    {
        var product = new EntityFiller(maxDepth: 2).Create<Product>();
        EntityFiller.Set(product, nameof(Product.ImageUrl), "/uploads/products/t1/cafe");
        EntityFiller.Set(product, nameof(Product.PriceAmount), 30m);
        EntityFiller.Set(product, nameof(Product.CompareAtPrice), 35m);

        var dto = Project(product, CatalogMappings.AsDto);

        Assert.Equal("/uploads/products/t1/cafe_md.webp", dto.ImageUrl);
        Assert.Equal("/uploads/products/t1/cafe_xs.webp", dto.Images!.Thumbnail);
        Assert.True(dto.HasDiscount);
        // Low stock comes from the inventory ledger (ProductStockReader), never from what was captured by hand.
        Assert.False(dto.IsLowStock);
    }

    [Fact]
    public void TenantWithoutSettings_ShouldGetTheDefaultsOfANewBusiness()
    {
        var tenant = new EntityFiller(maxDepth: 1).Create<Tenant>();
        EntityFiller.Set(tenant, nameof(Tenant.Settings), null);
        EntityFiller.Set(tenant, nameof(Tenant.Address), null);

        var profile = tenant.ToProfileDto();

        Assert.Equal(TenantCapability.None, profile.Capabilities);
        Assert.Equal(VatRate.GeneralPercent, profile.DefaultTaxRate);
        Assert.True(profile.AutoCloseCounterSales);
        Assert.Equal(InsufficientStockPolicy.Block, profile.InsufficientStockPolicy);
        Assert.Equal(OnlineMenuMode.Off, profile.OnlineMenuMode);
        Assert.Null(profile.Street);
    }

    private static TDto Project<TEntity, TDto>(TEntity entity, System.Linq.Expressions.Expression<Func<TEntity, TDto>> projection)
        => new[] { entity }.AsQueryable().Select(projection).Single();

    private static void Same<T>(T expected, T actual)
        => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
