using System.Text.Json;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Audit.DTOs;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The lists project in SQL with hand-written expressions (no AutoMapper): SQL Server must translate them, and they must
/// return what the in-memory mapping returns for the same rows loaded with their children.
/// </summary>
public class DtoProjectionSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task OrderList_ShouldBeComputedBySqlServer_AsTheLoadedOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid tenantId;
        await using (var seed = database.CreateContext())
            tenantId = await SeedOrdersAsync(seed);

        await using var context = database.CreateContext(tenantId);
        var listed = await context.Orders.AsNoTracking()
            .OrderBy(o => o.OrderNumber)
            .Select(OrderMappings.AsDto)
            .ToListAsync();
        var loaded = await context.Orders.AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Options)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .Include(o => o.KitchenTickets)
            .AsSplitQuery()
            .OrderBy(o => o.OrderNumber)
            .ToListAsync();

        Assert.Equal(3, listed.Count);
        Assert.Equal(Normalize(loaded.Select(o => o.ToDto())), Normalize(listed));
        var sale = listed.Single(o => o.OrderNumber == "ORD-000001");
        Assert.Equal(5m, sale.TipTotal);
        Assert.Equal(sale.Total - 100m, sale.Balance);
        Assert.Equal(200m - 100m - 5m, sale.Payments.Single().Change);
        Assert.Equal(2, sale.Items.Single(i => i.Options.Count > 0).Options.Count);
        Assert.True(listed.Single(o => o.OrderNumber == "ORD-000003").IsOpenTab);

        // The list endpoint pages it.
        var tenant = new InMemoryApplicationDbContext.TestCurrentTenantService();
        tenant.SetTenant(tenantId);
        var page = await new GetOrdersByBusinessQueryHandler(context, tenant)
            .Handle(new GetOrdersByBusinessQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, o => Assert.NotEmpty(o.Items));
        Assert.All(page.Items, o => Assert.NotEmpty(o.TaxBreakdown));
    }

    [SqlServerFact]
    public async Task OtherLists_ShouldBeComputedBySqlServer_AsTheLoadedRows()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid tenantId;
        await using (var seed = database.CreateContext())
            tenantId = await SeedCatalogAndChatAsync(seed);

        await using var context = database.CreateContext(tenantId);
        var products = await context.Products.AsNoTracking().Select(CatalogMappings.AsDto).ToListAsync();
        var loadedProduct = await context.Products.AsNoTracking()
            .Include(p => p.OptionGroups).ThenInclude(g => g.Options)
            .SingleAsync();
        var tenants = await context.Tenants.AsNoTracking().IgnoreQueryFilters().Select(TenantMappings.AsDto).ToListAsync();
        var locations = await context.InventoryLocations.AsNoTracking().Select(InventoryMappings.AsDto).ToListAsync();
        var agents = await context.AiAgents.AsNoTracking().Select(AiAgentMappings.AsDto).ToListAsync();
        var logs = await context.AuditLogs.AsNoTracking().Select(AuditLogMappings.AsDto).ToListAsync();
        var conversations = await context.WhatsAppConversations.AsNoTracking().Select(WhatsAppMappings.ConversationAsDto).ToListAsync();
        var messages = await context.WhatsAppMessages.AsNoTracking().Select(WhatsAppMappings.MessageAsDto).ToListAsync();

        Assert.Equal(Json(loadedProduct.ToDto()), Json(Assert.Single(products)));
        Assert.True(products[0].HasDiscount);
        // Low stock is read from the inventory ledger (ProductStockSqlServerTests), not from what was captured by hand.
        Assert.False(products[0].IsLowStock);
        Assert.Equal("/uploads/products/demo/latte_md.webp", products[0].ImageUrl);
        Assert.Equal(TenantCapability.Food, Assert.Single(tenants, t => t.Id == tenantId).Capabilities);
        Assert.Equal(Json((await context.InventoryLocations.AsNoTracking().SingleAsync()).ToDto()), Json(Assert.Single(locations)));
        Assert.Equal(Json((await context.AiAgents.AsNoTracking().SingleAsync()).ToDto()), Json(Assert.Single(agents)));
        Assert.Equal("Order", Assert.Single(logs).EntityName);
        Assert.Equal(Json((await context.WhatsAppConversations.AsNoTracking().SingleAsync()).ToDto()), Json(Assert.Single(conversations)));
        Assert.Equal(nameof(MessageDirection.Inbound), Assert.Single(messages).Direction);
    }

    private static async Task<Guid> SeedOrdersAsync(ApplicationDbContext context)
    {
        var tenant = new Tenant("Café", "cafe-" + Guid.NewGuid().ToString("N")[..8], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Food | TenantCapability.Service));

        // A paid sale with options, a tip, the cash handed over and a refund of one unit.
        var sale = NewOrder(tenant.Id, "ORD-000001", OrderType.Retail);
        sale.AddItem(Guid.NewGuid(), "Latte", 2, 60m, options:
        [
            new OrderLineOption(Guid.NewGuid(), "Tamaño", "Grande", 12m),
            new OrderLineOption(Guid.NewGuid(), "Leche", "Almendra", 8m)
        ]);
        sale.AddItem(Guid.NewGuid(), "Concha", 1, 18m);
        sale.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "cajero");
        sale.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 5m, cashReceived: 200m);
        var concha = sale.Items.Single(i => i.ProductName == "Concha");
        sale.RegisterRefund(18m, OrderPaymentMethod.Cash, "Se cayó", Now, "cajero", null, null, false, [(concha.Id, 1m)]);

        // A service with its intake and a piece.
        var service = NewOrder(tenant.Id, "ORD-000002", OrderType.Service);
        service.AddItem(Guid.NewGuid(), "Lavado", 1, 90m);
        service.SetServiceDetail(Now, Now.AddDays(1), 1, "Mancha", null, null, null, null);
        service.AddServicePiece("Saco", "Zara", null, "Negro", "M", null, null, "Lavado", null);

        // An open tab with its first round in the kitchen.
        var tab = NewOrder(tenant.Id, "ORD-000003", OrderType.Retail);
        tab.AddItem(Guid.NewGuid(), "Taco", 3, 22m);
        tab.OpenAsTab(null, "Mesa 4", Now, "mesero");
        tab.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "mesero");

        context.Orders.AddRange(sale, service, tab);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<Guid> SeedCatalogAndChatAsync(ApplicationDbContext context)
    {
        var tenant = new Tenant("Café", "cafe-" + Guid.NewGuid().ToString("N")[..8], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
        context.Tenants.Add(tenant);
        context.TenantSettings.Add(new TenantSettings(tenant.Id, TenantCapability.Food));
        var category = new Category(tenant.Id, "Bebidas", null);
        var latte = new Product(tenant.Id, category.Id, "Latte", null, 45m, "MXN",
            imageUrl: "/uploads/products/demo/latte", compareAtPrice: 50m);
        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1, [new(null, "Chico", -5m), new(null, "Grande", 12m)]),
            new ProductOptionGroupDefinition(null, "Extras", 0, 2, [new(null, "Shot", 10m), new(null, "Canela", 0m)])
        ]);
        context.Categories.Add(category);
        context.Products.Add(latte);
        context.InventoryLocations.Add(new InventoryLocation(tenant.Id, "Mostrador", "MAIN"));
        context.AiAgents.Add(new AiAgent(tenant.Id, "Asistente", "Ventas"));
        context.AuditLogs.Add(new AuditLog(tenant.Id, "Order", Guid.NewGuid().ToString(), "Created", "cajero", timestamp: Now));
        var conversation = new WhatsAppConversation(tenant.Id, "+525512345678", "Ana");
        conversation.AddMessage(MessageDirection.Inbound, "Hola", "wamid.1");
        context.WhatsAppConversations.Add(conversation);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static Order NewOrder(Guid tenantId, string number, OrderType orderType)
        => new(tenantId, null, null, true, number, BusinessType.Food, orderType, "Público en general", null, null, null);

    /// <summary>Children in a stable order: SQL Server does not promise the order of rows without ORDER BY.</summary>
    private static string Normalize(IEnumerable<OrderDto> orders)
        => Json(orders.Select(o =>
        {
            o.Items = o.Items.OrderBy(i => i.Id).ToList();
            o.StatusHistory = o.StatusHistory.OrderBy(h => h.Id).ToList();
            o.ServicePieces = o.ServicePieces.OrderBy(p => p.Id).ToList();
            o.Payments = o.Payments.OrderBy(p => p.Id).ToList();
            o.Refunds = o.Refunds.OrderBy(r => r.Id).ToList();
            return o;
        }).ToList());

    /// <summary>Same number, same text: SQL Server returns 0.00 where C# computes 0.</summary>
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new NormalizedDecimalConverter() } };

    private sealed class NormalizedDecimalConverter : System.Text.Json.Serialization.JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture));
    }
}
