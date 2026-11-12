using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Commands;
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
/// The cart and the postal code of a customer account (Fase 31J, ADR 0090): they follow the customer to another device. The
/// account keeps a cart per store, only its own, the most recent ten of the last week, in stores that are published; an
/// order of the account empties the cart of its store. An emptied cart stays, empty and with when, so a device that kept it
/// from before forgets its own instead of sending it back. Only a customer account keeps them.
/// </summary>
public class AccountCartFlowTests
{
    [Fact]
    public async Task Cart_ShouldBeKept_Replaced_AndEmptied_WithWhatWasChosen()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture, "demo");
        var salsa = Guid.NewGuid();
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));

        var saved = await SaveAsync(fixture, "demo", [Line(tacos.Id, 2, " sin cebolla ", salsa)], reorderOf: 3);
        var carts = await CartsAsync(fixture);

        Assert.True(saved.IsSuccess, saved.Error);
        var cart = Assert.Single(carts);
        Assert.Equal(("demo", "Tacos Demo", SalesTestFixture.Now, 3, (int?)null), (cart.Slug, cart.BusinessName, cart.SavedAt, cart.ReorderOf, cart.Skipped));
        var line = Assert.Single(cart.Lines);
        Assert.Equal((tacos.Id, 2, "sin cebolla"), (line.ProductId, line.Quantity, line.Notes));
        Assert.Equal([salsa], line.OptionIds);

        // From another device, later: it is replaced whole.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(2);
        await SaveAsync(fixture, "demo", [Line(tacos.Id, 5)]);
        cart = Assert.Single(await CartsAsync(fixture));
        Assert.Equal((5, (string?)null, SalesTestFixture.Now.AddHours(2), (int?)null), (cart.Lines.Single().Quantity, cart.Lines.Single().Notes, cart.SavedAt, cart.ReorderOf));

        // Emptied, it stays empty with when: a device that kept it from before (older) forgets its own.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(3);
        var emptied = await SaveAsync(fixture, "demo", []);
        Assert.True(emptied.IsSuccess, emptied.Error);
        Assert.Null(emptied.Value);
        cart = Assert.Single(await CartsAsync(fixture));
        Assert.Equal(("demo", SalesTestFixture.Now.AddHours(3), 0, (int?)null), (cart.Slug, cart.SavedAt, cart.Lines.Count, cart.ReorderOf));
        Assert.Empty((await fixture.Context.AccountCarts.SingleAsync()).Lines);

        // An emptied store that had none keeps none; saving another store keeps the emptied one (a week, as a device).
        var (_, cafe) = await StoreAsync(fixture, "cafe");
        Assert.True((await SaveAsync(fixture, "cafe", [])).IsSuccess);
        Assert.Equal(1, await fixture.Context.AccountCarts.CountAsync());
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(4);
        await SaveAsync(fixture, "cafe", [Line(cafe.Id, 1)]);
        Assert.Equal([("cafe", 1), ("demo", 0)], (await CartsAsync(fixture)).Select(c => (c.Slug, c.Lines.Count)));
    }

    [Fact]
    public async Task Cart_ShouldBeOnlyTheAccounts_InAPublishedStore()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture, "demo");
        await StoreAsync(fixture, "sin-menu", OnlineMenuMode.Off);
        var ana = await AccountAsync(fixture, "ana@demo.com");
        var beto = await AccountAsync(fixture, "beto@demo.com");

        SignIn(fixture, ana);
        await SaveAsync(fixture, "demo", [Line(tacos.Id, 1)]);
        Assert.Equal(AccountCartRules.NotFoundMessage, (await SaveAsync(fixture, "sin-menu", [Line(tacos.Id, 1)])).Error);
        Assert.Equal(AccountCartRules.NotFoundMessage, (await SaveAsync(fixture, "no-existe", [Line(tacos.Id, 1)])).Error);

        // Beto does not see Ana's, nor forgets it.
        SignIn(fixture, beto);
        Assert.Empty(await CartsAsync(fixture));
        Assert.True((await DeleteAsync(fixture, "demo")).IsSuccess);
        SignIn(fixture, ana);
        Assert.Single(await CartsAsync(fixture));

        // A team account and a request without a session buy as guests: their carts stay on the device.
        SignIn(fixture, await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin));
        Assert.Equal(AccountCartRules.CustomersOnlyMessage, (await SaveAsync(fixture, "demo", [Line(tacos.Id, 1)])).Error);
        Assert.Empty(await CartsAsync(fixture));
        fixture.CurrentUser.UserId = null;
        Assert.Equal(AccountCartRules.CustomersOnlyMessage, (await DeleteAsync(fixture, "demo")).Error);
        Assert.Equal(1, await fixture.Context.AccountCarts.CountAsync());
    }

    [Fact]
    public async Task Carts_ShouldBe_TheMostRecentTen_OfTheLastWeek()
    {
        await using var fixture = SalesTestFixture.Create();
        var stores = new List<(Tenant Tenant, Product Product)>();
        for (var i = 0; i < AccountCart.MaxPerAccount + 1; i++)
            stores.Add(await StoreAsync(fixture, $"tienda-{i:00}"));
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));

        // One a day... and the first one more than a week before the last.
        for (var i = 0; i < stores.Count; i++)
        {
            fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(i * 16);
            await SaveAsync(fixture, $"tienda-{i:00}", [Line(stores[i].Product.Id, 1)]);
        }

        var carts = await CartsAsync(fixture);
        Assert.Equal(Enumerable.Range(1, AccountCart.MaxPerAccount).Reverse().Select(i => $"tienda-{i:00}"), carts.Select(c => c.Slug));
        Assert.Equal(AccountCart.MaxPerAccount, await fixture.Context.AccountCarts.CountAsync());

        // A week without changing it, a cart is no longer offered; the next save takes it away.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1 * 16).AddDays(AccountCart.FreshDays);
        Assert.DoesNotContain("tienda-01", (await CartsAsync(fixture)).Select(c => c.Slug));
        await SaveAsync(fixture, "tienda-10", [Line(stores[10].Product.Id, 3)]);
        Assert.Equal(AccountCart.MaxPerAccount - 1, await fixture.Context.AccountCarts.CountAsync());

        // An emptied one counts as the device's would, and goes too after a week.
        await SaveAsync(fixture, "tienda-02", []);
        Assert.Contains("tienda-02", (await CartsAsync(fixture)).Select(c => c.Slug));
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(AccountCart.FreshDays);
        Assert.Empty(await CartsAsync(fixture));
        await SaveAsync(fixture, "tienda-03", [Line(stores[3].Product.Id, 1)]);
        Assert.Equal([stores[3].Tenant.Id], await fixture.Context.AccountCarts.Select(c => c.BusinessId).ToListAsync());
    }

    [Fact]
    public async Task Cart_OfAStoreSuspendedOrUnpublished_ShouldBeLeftOut_UntilItIsBack_AndStillBeEmptied()
    {
        await using var fixture = SalesTestFixture.Create();
        var (store, tacos) = await StoreAsync(fixture, "demo");
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));
        await SaveAsync(fixture, "demo", [Line(tacos.Id, 1)]);

        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == store.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Off);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await CartsAsync(fixture));

        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        await fixture.Context.SaveChangesAsync();
        Assert.Single(await CartsAsync(fixture));
        fixture.Context.ChangeTracker.Clear();

        // Suspended (for billing) with its menu published, the same: left out, and a save there is refused; active again,
        // it is back as it was.
        await SetStatusAsync(fixture, store.Id, TenantStatus.Suspended);
        Assert.Empty(await CartsAsync(fixture));
        Assert.Equal(AccountCartRules.NotFoundMessage, (await SaveAsync(fixture, "demo", [Line(tacos.Id, 2)])).Error);
        await SetStatusAsync(fixture, store.Id, TenantStatus.Active);
        Assert.Equal(1, Assert.Single(await CartsAsync(fixture)).Lines.Single().Quantity);

        // Suspended and unpublished, it is still emptied by its slug.
        await SetStatusAsync(fixture, store.Id, TenantStatus.Suspended);
        settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == store.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Off);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);
        Assert.True((await DeleteAsync(fixture, " DEMO ")).IsSuccess);
        var emptied = await fixture.Context.AccountCarts.AsNoTracking().SingleAsync();
        Assert.Equal((0, SalesTestFixture.Now.AddMinutes(5)), (emptied.Lines.Count, emptied.SavedAt));
    }

    [Fact]
    public async Task OrderOfTheAccount_ShouldEmpty_TheCartOfItsStore_AndKeepTheOthers()
    {
        await using var fixture = SalesTestFixture.Create();
        var (_, tacos) = await StoreAsync(fixture, "demo", drawer: true);
        var (_, cafe) = await StoreAsync(fixture, "cafe");
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        var beto = await AccountAsync(fixture, "beto@demo.com");
        SignIn(fixture, beto);
        await SaveAsync(fixture, "demo", [Line(tacos.Id, 1)]);
        SignIn(fixture, mariana);
        await SaveAsync(fixture, "demo", [Line(tacos.Id, 2)]);
        await SaveAsync(fixture, "cafe", [Line(cafe.Id, 1)]);

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(10);
        var receipt = await PlaceAsync(fixture, tacos);

        Assert.True(receipt.IsSuccess, receipt.Error);
        // Ordered, it stays empty with when: the device that kept it from before forgets its own.
        var carts = await CartsAsync(fixture);
        Assert.Equal([("cafe", 1), ("demo", 0)], carts.Select(c => (c.Slug, c.Lines.Count)).OrderBy(c => c.Item1));
        Assert.Equal(SalesTestFixture.Now.AddMinutes(10), carts.Single(c => c.Slug == "demo").SavedAt);
        // Beto's cart in the same store is his.
        SignIn(fixture, beto);
        Assert.Equal(1, Assert.Single(await CartsAsync(fixture)).Lines.Single().Quantity);

        // A guest order empties no account's cart.
        fixture.CurrentUser.UserId = null;
        Assert.True((await PlaceAsync(fixture, tacos, phone: "55 8765 4321")).IsSuccess);
        SignIn(fixture, beto);
        Assert.Single(Assert.Single(await CartsAsync(fixture)).Lines);
    }

    [Fact]
    public async Task PostalCode_ShouldFollowTheAccount_AndBeOnlyACustomers()
    {
        await using var fixture = SalesTestFixture.Create();
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);

        Assert.Null((await PostalCodeAsync(fixture)).PostalCode);
        var saved = await SavePostalCodeAsync(fixture, " 72000 ");
        Assert.Equal("72000", saved.Value!.PostalCode);
        Assert.Equal("72000", (await PostalCodeAsync(fixture)).PostalCode);
        Assert.Equal("72000", (await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == mariana.Id)).PostalCode);

        Assert.Null((await SavePostalCodeAsync(fixture, "")).Value!.PostalCode);
        Assert.Null((await fixture.Context.Users.AsNoTracking().SingleAsync(u => u.Id == mariana.Id)).PostalCode);

        // Five digits, and the message does not repeat what was written (ADR 0017).
        var invalid = await new SaveMyPostalCodeCommandValidator().ValidateAsync(new SaveMyPostalCodeCommand("7200a"));
        Assert.Equal([GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage], invalid.Errors.Select(e => e.ErrorMessage));
        Assert.True((await new SaveMyPostalCodeCommandValidator().ValidateAsync(new SaveMyPostalCodeCommand(null))).IsValid);

        SignIn(fixture, await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin));
        Assert.Equal(AccountCartRules.CustomersOnlyMessage, (await SavePostalCodeAsync(fixture, "72000")).Error);
        Assert.Null((await PostalCodeAsync(fixture)).PostalCode);
    }

    [Fact]
    public void Validator_ShouldRefuse_ALineThatCannotBe_MoreThanFiftyLines_AndMoreThanItsOptions()
    {
        var validator = new SaveMyCartCommandValidator();
        SaveMyCartCommand With(params MyCartLineDto[] lines) => new() { Slug = "demo", Lines = [.. lines] };

        Assert.True(validator.Validate(With(Line(Guid.NewGuid(), 99, new string('a', 300)))).IsValid);
        foreach (var bad in new[]
        {
            Line(Guid.Empty, 1), Line(Guid.NewGuid(), 0), Line(Guid.NewGuid(), 100), Line(Guid.NewGuid(), 1, new string('a', 301)),
            new MyCartLineDto { ProductId = Guid.NewGuid(), Quantity = 1, OptionIds = null! },
            // {"lines":[null]}: a 400, not a failure of the server.
            null!
        })
            Assert.Contains(AccountCart.BadLineMessage, validator.Validate(With(bad)).Errors.Select(e => e.ErrorMessage));
        var fiftyOne = Enumerable.Range(0, AccountCart.MaxLines + 1).Select(_ => Line(Guid.NewGuid(), 1)).ToArray();
        Assert.Contains(AccountCart.TooManyLinesMessage, validator.Validate(With(fiftyOne)).Errors.Select(e => e.ErrorMessage));

        // The options of all its lines: up to 500, however they are spread.
        MyCartLineDto WithOptions(int count) => Line(Guid.NewGuid(), 1, null, [.. Enumerable.Range(0, count).Select(_ => Guid.NewGuid())]);
        var full = Enumerable.Range(0, 2).Select(_ => WithOptions(AccountCart.MaxOptionsPerCart / 2)).ToArray();
        Assert.True(validator.Validate(With(full)).IsValid);
        var over = full.Append(WithOptions(1)).ToArray();
        Assert.Equal([AccountCart.TooManyOptionsMessage], validator.Validate(With(over)).Errors.Select(e => e.ErrorMessage));
    }

    private static MyCartLineDto Line(Guid productId, int quantity, string? notes = null, params Guid[] options)
        => new() { ProductId = productId, Quantity = quantity, Notes = notes, OptionIds = [.. options] };

    private static async Task<(Tenant Tenant, Product Product)> StoreAsync(
        SalesTestFixture fixture, string slug, OnlineMenuMode mode = OnlineMenuMode.Orders, bool drawer = false)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        tenant.Update(slug == "demo" ? "Tacos Demo" : $"Tienda {slug}", $"{slug}@demo.com", null, null);
        var product = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(mode);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: true, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
        if (drawer)
            await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        return (tenant, product);
    }

    private static async Task<ApplicationUser> AccountAsync(SalesTestFixture fixture, string email, UserRole role = UserRole.Customer)
    {
        var account = new ApplicationUser { UserName = email, Email = email, FirstName = "Mariana", LastName = "Ortiz", Role = role, IsActive = true };
        fixture.Context.Users.Add(account);
        await fixture.Context.SaveChangesAsync();
        return account;
    }

    private static async Task SetStatusAsync(SalesTestFixture fixture, Guid businessId, TenantStatus status)
    {
        var business = await fixture.Context.Tenants.SingleAsync(t => t.Id == businessId);
        business.SetStatus(status);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static void SignIn(SalesTestFixture fixture, ApplicationUser account)
    {
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = account.Role == UserRole.Customer ? "CUSTOMER" : "TENANT_ADMIN";
    }

    private static AccountCartHandlers Handlers(SalesTestFixture fixture) => new(fixture.Context, fixture.CurrentUser, fixture.Clock);

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<MyCartDto?>> SaveAsync(
        SalesTestFixture fixture, string slug, List<MyCartLineDto> lines, int? reorderOf = null)
    {
        var result = await Handlers(fixture).Handle(new SaveMyCartCommand { Slug = slug, Lines = lines, ReorderOf = reorderOf }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result> DeleteAsync(SalesTestFixture fixture, string slug)
    {
        var result = await Handlers(fixture).Handle(new DeleteMyCartCommand(slug), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<List<MyCartDto>> CartsAsync(SalesTestFixture fixture)
        => Handlers(fixture).Handle(new GetMyCartsQuery(), CancellationToken.None);

    private static Task<MyPostalCodeDto> PostalCodeAsync(SalesTestFixture fixture)
        => Handlers(fixture).Handle(new GetMyPostalCodeQuery(), CancellationToken.None);

    private static async Task<Result<MyPostalCodeDto>> SavePostalCodeAsync(SalesTestFixture fixture, string? postalCode)
    {
        var result = await Handlers(fixture).Handle(new SaveMyPostalCodeCommand(postalCode), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result<PublicOrderReceiptDto>> PlaceAsync(SalesTestFixture fixture, Product product, string phone = "55 1234 5678")
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var result = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context), fixture.CurrentUser)
            .Handle(new PlacePublicOrderCommand
            {
                Slug = "demo",
                CustomerName = "Mariana Ortiz",
                CustomerPhone = phone,
                Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
            }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }
}
