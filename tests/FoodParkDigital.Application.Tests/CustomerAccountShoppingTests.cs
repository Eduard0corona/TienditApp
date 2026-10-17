using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Buying with a customer account (Fase 24B): the session says whose order it is, without confirming the phone; the same
/// order sent twice with its key is placed once; the account keeps its addresses, only its own, at most ten, and offers
/// the last one used first; «Pedir otra vez» knows each product and its options.
/// </summary>
public class CustomerAccountShoppingTests
{
    private const string Address = "Av. Reforma 123, Centro";

    [Fact]
    public async Task Order_WithACustomerSession_ShouldBeTheAccounts_WithoutConfirmingThePhone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);

        var receipt = await PlaceAsync(fixture, Pickup(tacos));

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.True(receipt.Value.LinkedToAccount);
        var order = await fixture.Context.Orders.SingleAsync();
        Assert.Equal((mariana.Id, (Guid?)null), (order.CustomerUserId, order.TemporaryCustomerId));
        // Nobody who confirms that phone later claims it: there is no temporary customer to claim.
        Assert.Empty(await fixture.Context.TemporaryCustomers.IgnoreQueryFilters().ToListAsync());

        // «Pedir otra vez» knows the product it puts back in the cart.
        var mine = await new GetMyOrdersQueryHandler(fixture.Context, fixture.CurrentUser).Handle(new GetMyOrdersQuery(), CancellationToken.None);
        var item = Assert.Single(Assert.Single(mine.Items).Items);
        Assert.Equal((tacos.Id, "Orden de tacos", 1m), (item.ProductId, item.ProductName, item.Quantity));
        Assert.Empty(item.OptionIds);
    }

    [Fact]
    public async Task Order_WithASessionOfTheTeam_OrOfAnInactiveAccount_ShouldBeAGuestOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        SignIn(fixture, await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin));

        var byTheTeam = await PlaceAsync(fixture, Pickup(tacos));

        var closed = await AccountAsync(fixture, "cerrada@demo.com");
        await DeactivateAsync(fixture, closed);
        SignIn(fixture, closed);
        var byAnInactiveAccount = await PlaceAsync(fixture, Pickup(tacos) with { CustomerPhone = "5587654321" });

        Assert.True(byTheTeam.IsSuccess, byTheTeam.Error);
        Assert.True(byAnInactiveAccount.IsSuccess, byAnInactiveAccount.Error);
        Assert.False(byTheTeam.Value.LinkedToAccount);
        Assert.False(byAnInactiveAccount.Value.LinkedToAccount);
        var orders = await fixture.Context.Orders.ToListAsync();
        Assert.All(orders, order => Assert.Null(order.CustomerUserId));
        Assert.All(orders, order => Assert.NotNull(order.TemporaryCustomerId));
    }

    [Fact]
    public async Task Order_SentAgainWithItsKey_ShouldBePlacedOnce_AndAnswerOnlyItsCustomer()
    {
        await using var fixture = SalesTestFixture.Create();
        var (tenant, tacos) = await StoreAsync(fixture);
        var key = Guid.NewGuid();

        var first = await PlaceAsync(fixture, Pickup(tacos) with { IdempotencyKey = key });
        // The answer was lost and the store sends it again, after the business closed its drawer.
        await CloseDrawersAsync(fixture, tenant);
        var again = await PlaceAsync(fixture, Pickup(tacos) with { IdempotencyKey = key });
        var someoneElse = await PlaceAsync(fixture, Pickup(tacos) with { IdempotencyKey = key, CustomerPhone = "5587654321" });

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal((first.Value.OrderNumber, first.Value.Total), (again.Value.OrderNumber, again.Value.Total));
        Assert.Equal(PlacePublicOrderCommandHandler.AlreadySentMessage, someoneElse.Error);
        var order = await fixture.Context.Orders.SingleAsync();
        Assert.Equal(key, order.ClientRequestId);

        // Without the drawer, an order with a new key is refused as before.
        var paused = await PlaceAsync(fixture, Pickup(tacos) with { IdempotencyKey = Guid.NewGuid() });
        Assert.Equal(PlacePublicOrderCommandHandler.OrdersPausedMessage, paused.Error);
    }

    [Fact]
    public void OrderValidator_ShouldRefuseAnEmptyKey_AndALongAddressName()
    {
        var validator = new PlacePublicOrderCommandValidator();
        var order = new PlacePublicOrderCommand
        {
            Slug = "demo",
            CustomerName = "Ana",
            CustomerPhone = "5512345678",
            Items = [new CreateOrderItemDto { ProductId = Guid.NewGuid(), Quantity = 1 }]
        };

        Assert.True(validator.Validate(order with { IdempotencyKey = Guid.NewGuid(), AddressLabel = "Casa" }).IsValid);
        Assert.Equal("La clave del pedido no es válida.",
            Assert.Single(validator.Validate(order with { IdempotencyKey = Guid.Empty }).Errors).ErrorMessage);
        Assert.Equal(AccountAddressRules.LabelTooLongMessage,
            Assert.Single(validator.Validate(order with { AddressLabel = new string('a', 201) }).Errors).ErrorMessage);
    }

    [Fact]
    public async Task Delivery_WithASession_ShouldKeepTheAddressWhenAsked_AndOfferTheLastOneUsedFirst()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);

        var notKept = await PlaceAsync(fixture, ToAddress(tacos, "72000", Address));
        Assert.True(notKept.IsSuccess, notKept.Error);
        Assert.Empty(await AddressesAsync(fixture));

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);
        var kept = await PlaceAsync(fixture, ToAddress(tacos, "72000", Address, "Portón verde") with { SaveAddress = true, AddressLabel = " Casa " });
        Assert.True(kept.IsSuccess, kept.Error);
        var casa = Assert.Single(await AddressesAsync(fixture));
        Assert.Equal(("Casa", "72000", Address, "Portón verde", (DateTimeOffset?)fixture.Clock.UtcNow),
            (casa.Label, casa.PostalCode, casa.AddressLine, casa.References, casa.LastUsedAt));

        // The same place written another way is the same address: it is used again, with the references of this order.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(10);
        var reused = await PlaceAsync(fixture, ToAddress(tacos, "72000", "  av. REFORMA 123,  centro ", "Portón negro") with { SaveAddress = true });
        Assert.True(reused.IsSuccess, reused.Error);
        var used = Assert.Single(await AddressesAsync(fixture));
        Assert.Equal((casa.Id, "Casa", Address, "Portón negro", (DateTimeOffset?)fixture.Clock.UtcNow),
            (used.Id, used.Label, used.AddressLine, used.References, used.LastUsedAt));

        // A full account still orders: the new address only stays out.
        for (var i = 1; i < AccountAddress.MaxPerAccount; i++)
            fixture.Context.AccountAddresses.Add(AccountAddress.Create(mariana.Id, $"Dirección {i}", "72000", $"Calle {i}", null, i));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var full = await PlaceAsync(fixture, ToAddress(tacos, "72000", "Calle Nueva 1") with { SaveAddress = true });
        Assert.True(full.IsSuccess, full.Error);
        Assert.Equal(AccountAddress.MaxPerAccount, (await AddressesAsync(fixture)).Count);
        Assert.Equal(casa.Id, (await AddressesAsync(fixture))[0].Id);
    }

    [Fact]
    public async Task Delivery_WithoutASession_ShouldKeepNoAddress()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture);
        fixture.CurrentUser.UserId = null;

        var receipt = await PlaceAsync(fixture, ToAddress(tacos, "72000", Address) with { SaveAddress = true, AddressLabel = "Casa" });

        Assert.True(receipt.IsSuccess, receipt.Error);
        Assert.False(receipt.Value.LinkedToAccount);
        Assert.Empty(await fixture.Context.AccountAddresses.ToListAsync());
    }

    [Fact]
    public async Task Addresses_ShouldBeOnlyTheAccounts_OnePerPlace_AtMostTen()
    {
        await using var fixture = SalesTestFixture.Create();
        var ana = await AccountAsync(fixture, "ana@demo.com");
        var beto = await AccountAsync(fixture, "beto@demo.com");
        var owner = await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin);
        SignIn(fixture, ana);

        var casa = await SaveAsync(fixture, new SaveMyAddressCommand { Label = "Casa", PostalCode = "72000", AddressLine = Address });
        var again = await SaveAsync(fixture, new SaveMyAddressCommand { Label = "Mi casa", PostalCode = " 72000 ", AddressLine = "AV. REFORMA 123,  CENTRO", References = "Portón verde" });
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(1);
        var oficina = await SaveAsync(fixture, new SaveMyAddressCommand { Label = "Oficina", PostalCode = "72160", AddressLine = "Calle 5 de Mayo 8" });

        Assert.True(casa.IsSuccess && again.IsSuccess && oficina.IsSuccess);
        Assert.Equal(casa.Value.Id, again.Value.Id);
        Assert.Equal(("Mi casa", Address, "Portón verde"), (again.Value.Label, again.Value.AddressLine, again.Value.References));
        Assert.Equal(["Mi casa", "Oficina"], (await ListAsync(fixture)).Select(a => a.Label).Order());

        var onto = await UpdateAsync(fixture, new UpdateMyAddressCommand { Id = oficina.Value.Id, Label = "Oficina", PostalCode = "72000", AddressLine = Address });
        Assert.Equal("Ya guardaste esa dirección como «Mi casa».", onto.Error);
        var notAPostalCode = await SaveAsync(fixture, new SaveMyAddressCommand { PostalCode = "7200", AddressLine = "Calle 1" });
        Assert.Equal("«7200» no es un código postal: lleva 5 dígitos.", notAPostalCode.Error);

        // Another account finds none of them: it neither reads, changes nor deletes them.
        SignIn(fixture, beto);
        Assert.Empty(await ListAsync(fixture));
        var changed = await UpdateAsync(fixture, new UpdateMyAddressCommand { Id = casa.Value.Id, PostalCode = "72000", AddressLine = "Calle de Beto 1" });
        var deleted = await DeleteAsync(fixture, casa.Value.Id);
        Assert.Equal((AccountAddressRules.NotFoundMessage, AccountAddressRules.NotFoundMessage), (changed.Error, deleted.Error));
        // A business's team keeps none.
        SignIn(fixture, owner);
        Assert.Empty(await ListAsync(fixture));
        Assert.Equal(AccountAddressRules.CustomersOnlyMessage,
            (await SaveAsync(fixture, new SaveMyAddressCommand { PostalCode = "72000", AddressLine = Address })).Error);

        SignIn(fixture, ana);
        var ana2 = await ListAsync(fixture);
        Assert.Equal(2, ana2.Count);
        Assert.Equal(Address, ana2.Single(a => a.Id == casa.Value.Id).AddressLine);
        for (var i = 3; i <= AccountAddress.MaxPerAccount; i++)
            Assert.True((await SaveAsync(fixture, new SaveMyAddressCommand { PostalCode = "72000", AddressLine = $"Calle {i}" })).IsSuccess);
        var eleventh = await SaveAsync(fixture, new SaveMyAddressCommand { PostalCode = "72000", AddressLine = "Calle once" });
        Assert.Equal(AccountAddress.TooManyMessage, eleventh.Error);

        Assert.True((await DeleteAsync(fixture, oficina.Value.Id)).IsSuccess);
        Assert.DoesNotContain(await ListAsync(fixture), a => a.Id == oficina.Value.Id);
    }

    [Fact]
    public void AddressValidator_ShouldUseTheLimitsOfAnOrdersAddress()
    {
        var validator = new SaveMyAddressCommandValidator();

        Assert.True(validator.Validate(new SaveMyAddressCommand { Label = "Casa", PostalCode = "72000", AddressLine = Address }).IsValid);
        var empty = validator.Validate(new SaveMyAddressCommand());
        Assert.Equal(2, empty.Errors.Count);
        Assert.False(validator.Validate(new SaveMyAddressCommand { PostalCode = "72000", AddressLine = new string('a', 301) }).IsValid);
        Assert.False(validator.Validate(new SaveMyAddressCommand { PostalCode = "72000", AddressLine = Address, References = new string('a', 501) }).IsValid);
        Assert.False(validator.Validate(new SaveMyAddressCommand { Label = new string('a', 201), PostalCode = "72000", AddressLine = Address }).IsValid);
    }

    /// <summary>
    /// A kitchen that takes online orders and accepts them as they arrive, picked up or to 72000 (Centro: $35, from $150,
    /// free from $400).
    /// </summary>
    private static async Task<(Tenant Tenant, Product Tacos)> StoreAsync(SalesTestFixture fixture)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: "demo");
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var zone = await new CreateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new CreateDeliveryZoneCommand("Centro", 35m, 150m, 400m, ["72000"]), CancellationToken.None);
        Assert.True(zone.IsSuccess, zone.Error);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        // Accepted as they arrive: a phone may leave only three orders waiting.
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: true, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        fixture.Context.ChangeTracker.Clear();
        return (tenant, tacos);
    }

    private static async Task<ApplicationUser> AccountAsync(SalesTestFixture fixture, string email, UserRole role = UserRole.Customer)
    {
        var account = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Mariana",
            LastName = "Ortiz",
            Role = role,
            IsActive = true
        };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return account;
    }

    private static async Task DeactivateAsync(SalesTestFixture fixture, ApplicationUser account)
    {
        var stored = await fixture.Context.Users.SingleAsync(u => u.Id == account.Id);
        stored.IsActive = false;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task CloseDrawersAsync(SalesTestFixture fixture, Tenant tenant)
    {
        foreach (var drawer in await fixture.Context.CashSessions.IgnoreQueryFilters().Where(c => c.TenantId == tenant.Id).ToListAsync())
            fixture.Context.CashSessions.Remove(drawer);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static void SignIn(SalesTestFixture fixture, ApplicationUser account)
    {
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = account.Role == UserRole.Customer ? "CUSTOMER" : "TENANT_ADMIN";
    }

    private static PlacePublicOrderCommand Pickup(Product product) => new()
    {
        Slug = "demo",
        CustomerName = "Mariana Ortiz",
        CustomerPhone = "55 1234 5678",
        Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
    };

    private static PlacePublicOrderCommand ToAddress(Product product, string postalCode, string addressLine, string? references = null)
        => Pickup(product) with
        {
            Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 2 }],
            Delivery = new OrderDeliveryInput { PostalCode = postalCode, AddressLine = addressLine, References = references }
        };

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, PlacePublicOrderCommand command)
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context), fixture.CurrentUser)
            .Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<List<AccountAddress>> AddressesAsync(SalesTestFixture fixture)
        => AccountAddresses.OfAsync(fixture.Context, Guid.Parse(fixture.CurrentUser.UserId!), CancellationToken.None);

    private static Task<List<AccountAddressDto>> ListAsync(SalesTestFixture fixture)
        => new GetMyAddressesQueryHandler(fixture.Context, fixture.CurrentUser).Handle(new GetMyAddressesQuery(), CancellationToken.None);

    private static AccountAddressCommandHandlers Handlers(SalesTestFixture fixture) => new(fixture.Context, fixture.CurrentUser);

    private static async Task<Result<AccountAddressDto>> SaveAsync(SalesTestFixture fixture, SaveMyAddressCommand command)
    {
        var result = await Handlers(fixture).Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result<AccountAddressDto>> UpdateAsync(SalesTestFixture fixture, UpdateMyAddressCommand command)
    {
        var result = await Handlers(fixture).Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> DeleteAsync(SalesTestFixture fixture, Guid id)
    {
        var result = await Handlers(fixture).Handle(new DeleteMyAddressCommand(id), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }
}
