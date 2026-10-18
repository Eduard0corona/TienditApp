using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Explorar finds products and keeps favorites (Fase 24F, ADR 0028): a search finds the products the menus of its
/// businesses show, by any word of their name or description, a few of each business; a card carries the cover of its
/// business; and a customer account keeps the businesses it likes, only its own, while their menus are published.
/// </summary>
public class ExploreProductsAndFavoritesTests
{
    [Fact]
    public async Task Search_ShouldFindTheProductsOfTheMenus_ByAnyWordOfTheirNameOrDescription_WithoutAccents()
    {
        await using var fixture = SalesTestFixture.Create();
        // The taquería takes orders now, so Explorar shows it first.
        var guero = await BusinessAsync(fixture, "taqueria-guero", "Taquería El Güero", drawer: true);
        await ProductAsync(fixture, guero, "Orden de pastor", 85m, description: "Cinco tacos con PIÑA y cilantro");
        await ProductAsync(fixture, guero, "Agua de horchata", 35m);
        var luna = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        await ProductAsync(fixture, luna, "Pastel de chocolate", 60m, compareAtPrice: 75m, image: "/uploads/products/luna/pastel");
        await ProductAsync(fixture, luna, "Gringa con piña", 70m);

        var pastor = await SearchAsync(fixture, "PASTOR");
        var pina = await SearchAsync(fixture, "pina");

        var orden = Assert.Single(pastor.Products);
        Assert.Equal(("Orden de pastor", 85m, "Taquería El Güero", "taqueria-guero"), (orden.Name, orden.Price, orden.BusinessName, orden.BusinessSlug));
        Assert.Null(orden.CompareAtPrice);
        Assert.Null(orden.ImageUrl);
        // The name first, even over the business Explorar shows first: the gringa says it in its name, the order only in
        // its description.
        Assert.Equal(["Gringa con piña", "Orden de pastor"], pina.Products.Select(p => p.Name));

        var pastel = Assert.Single((await SearchAsync(fixture, "pastel")).Products);
        Assert.Equal(75m, pastel.CompareAtPrice);
        Assert.Equal("/uploads/products/luna/pastel_sm.webp", pastel.ImageUrl);
        // The businesses still answer the search by their own name, description and city, as before.
        Assert.Empty(pastor.Businesses.Items);
        Assert.Equal("cafe-luna", Assert.Single((await SearchAsync(fixture, "luna")).Businesses.Items).Slug);
    }

    [Fact]
    public async Task Search_ShouldShowAFewProductsOfEachBusiness_TheOnesTakingOrdersNowFirst()
    {
        await using var fixture = SalesTestFixture.Create();
        var closed = await BusinessAsync(fixture, "a-tacos-cerrados", "A Tacos sin caja");
        var open = await BusinessAsync(fixture, "z-tacos-abiertos", "Z Tacos con caja", drawer: true);
        for (var i = 1; i <= 5; i++)
        {
            await ProductAsync(fixture, closed, $"Taco {i}", 20m);
            await ProductAsync(fixture, open, $"Taco {i}", 20m);
        }

        var products = (await SearchAsync(fixture, "taco")).Products;

        Assert.Equal(2 * PublicProductIndex.MaxPerBusiness, products.Count);
        // The business that takes orders now goes first, as in Explorar.
        Assert.Equal(
            ["z-tacos-abiertos", "z-tacos-abiertos", "z-tacos-abiertos", "a-tacos-cerrados", "a-tacos-cerrados", "a-tacos-cerrados"],
            products.Select(p => p.BusinessSlug));
        Assert.True(products[0].AcceptsOrders);
        Assert.False(products[^1].AcceptsOrders);
        Assert.Equal(["Taco 1", "Taco 2", "Taco 3"], products.Take(3).Select(p => p.Name));
    }

    [Fact]
    public async Task Search_ShouldStopAtTwelveProducts()
    {
        await using var fixture = SalesTestFixture.Create();
        for (var b = 1; b <= 5; b++)
        {
            var business = await BusinessAsync(fixture, $"tacos-{b}", $"Tacos {b}");
            for (var i = 1; i <= PublicProductIndex.MaxPerBusiness; i++)
                await ProductAsync(fixture, business, $"Taco {i}", 20m);
        }

        Assert.Equal(PublicProductIndex.MaxResults, (await SearchAsync(fixture, "taco")).Products.Count);
    }

    [Fact]
    public async Task Search_ShouldFindOnlyWhatTheMenusOfExplorarShow_InTheCategoryAsked()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        await ProductAsync(fixture, cafe, "Café de olla", 30m);
        var unavailable = await ProductAsync(fixture, cafe, "Café frío", 45m);
        unavailable.SetAvailability(false);
        // A service is booked, not ordered from the menu.
        await ProductAsync(fixture, cafe, "Café para eventos", 900m, unit: UnitType.Service);
        var hiddenCategory = new Category(cafe.Id, "Temporada", null);
        hiddenCategory.Deactivate();
        fixture.Context.Categories.Add(hiddenCategory);
        fixture.Context.Products.Add(new Product(cafe.Id, hiddenCategory.Id, "Café de calabaza", null, 50m, "MXN"));
        // A business its owner keeps out of Explorar.
        var hidden = await BusinessAsync(fixture, "cafe-oculto", "Café Oculto");
        await ProductAsync(fixture, hidden, "Café americano", 30m);
        (await SettingsAsync(fixture, hidden)).SetDirectoryListing(false);
        var hardware = await BusinessAsync(fixture, "ferre-sol", "Ferretería Sol", BusinessCategory.Hardware);
        await ProductAsync(fixture, hardware, "Café molido en bolsa", 120m);
        await fixture.Context.SaveChangesAsync();

        var all = await SearchAsync(fixture, "cafe");
        var food = await SearchAsync(fixture, "cafe", BusinessCategory.Food);

        Assert.Equal(["Café de olla", "Café molido en bolsa"], all.Products.Select(p => p.Name).Order());
        Assert.Equal("Café de olla", Assert.Single(food.Products).Name);
    }

    [Fact]
    public async Task Search_ShouldFindOnlyTheProductsItsMenuShows_TheFirstOnesInItsOrder()
    {
        await using var fixture = SalesTestFixture.Create();
        var ferre = await BusinessAsync(fixture, "ferre-sol", "Ferretería Sol");
        var category = await fixture.Context.Categories.IgnoreQueryFilters().FirstAsync(c => c.TenantId == ferre.Id);
        // A full menu: its featured products first. A service is not on it, so it does not take the place of a product.
        for (var i = 1; i < GetPublicMenuQueryHandler.MaxProducts; i++)
            fixture.Context.Products.Add(new Product(ferre.Id, category.Id, $"Clavo {i:000}", null, 1m, "MXN", isFeatured: true));
        fixture.Context.Products.Add(new Product(ferre.Id, category.Id, "Asesoría de obra", null, 500m, "MXN", isFeatured: true, unit: UnitType.Service));
        fixture.Context.Products.Add(new Product(ferre.Id, category.Id, "Tornillo de la casa", null, 2m, "MXN", isFeatured: true));
        // Past what the menu shows (with the product every business of these tests has).
        fixture.Context.Products.Add(new Product(ferre.Id, category.Id, "Tornillo de bodega", null, 2m, "MXN"));
        await fixture.Context.SaveChangesAsync();

        var found = await SearchAsync(fixture, "tornillo");
        var menu = await new GetPublicMenuQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicMenuQuery("ferre-sol"), CancellationToken.None);

        var shown = menu!.Categories.SelectMany(c => c.Products).Select(p => p.Name).ToList();
        Assert.Equal(GetPublicMenuQueryHandler.MaxProducts, shown.Count);
        Assert.Contains("Tornillo de la casa", shown);
        Assert.DoesNotContain("Tornillo de bodega", shown);
        // What a search finds opens in its menu: never one the menu does not show.
        Assert.Equal("Tornillo de la casa", Assert.Single(found.Products).Name);
    }

    [Fact]
    public async Task Search_ShouldSayWhenTheOrderAddsIva_NotForAProductExemptOrAtZero()
    {
        await using var fixture = SalesTestFixture.Create();
        var withoutIva = await BusinessAsync(fixture, "abarrotes-sin-iva", "Abarrotes sin IVA");
        (await SettingsAsync(fixture, withoutIva)).UpdateSalesPolicy(pricesIncludeTax: false, defaultTaxRate: 16m, autoCloseCounterSales: true);
        var category = await fixture.Context.Categories.IgnoreQueryFilters().FirstAsync(c => c.TenantId == withoutIva.Id);
        fixture.Context.Products.Add(new Product(withoutIva.Id, category.Id, "Jabón zz1", null, 20m, "MXN"));
        fixture.Context.Products.Add(new Product(withoutIva.Id, category.Id, "Leche zz1", null, 28m, "MXN", taxRate: 0m));
        fixture.Context.Products.Add(new Product(withoutIva.Id, category.Id, "Libro zz1", null, 150m, "MXN", isTaxExempt: true));
        var withIva = await BusinessAsync(fixture, "abarrotes-con-iva", "Abarrotes con IVA");
        (await SettingsAsync(fixture, withIva)).UpdateSalesPolicy(pricesIncludeTax: true, defaultTaxRate: 16m, autoCloseCounterSales: true);
        await ProductAsync(fixture, withIva, "Jabón zz2", 23m);
        await fixture.Context.SaveChangesAsync();

        var found = (await SearchAsync(fixture, "zz1")).Products.ToDictionary(p => p.Name, p => p.AddsTax);
        var included = Assert.Single((await SearchAsync(fixture, "zz2")).Products);

        Assert.Equal(new Dictionary<string, bool> { ["Jabón zz1"] = true, ["Leche zz1"] = false, ["Libro zz1"] = false }, found);
        Assert.False(included.AddsTax);
    }

    [Fact]
    public async Task Search_ShouldAnswerWithTheBusinesses_WhenItsProductsCannotBeRead()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        await ProductAsync(fixture, cafe, "Café de olla", 30m);
        var directory = new PublicDirectoryCache();
        var products = new PublicProductIndexCache();
        var snapshot = await directory.GetAsync(
            fixture.Clock.UtcNow, (utcNow, ct) => PublicDirectorySnapshot.BuildAsync(fixture.Context, utcNow, ct), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => products.GetAsync(
            snapshot, (_, _) => throw new InvalidOperationException("La base no respondió."), CancellationToken.None));

        var found = await new GetPublicDirectoryQueryHandler(
                fixture.Context, fixture.Clock, directory, products, NullLogger<GetPublicDirectoryQueryHandler>.Instance)
            .Handle(new GetPublicDirectoryQuery(Search: "cafe"), CancellationToken.None);

        Assert.Empty(found.Products);
        Assert.Equal("cafe-luna", Assert.Single(found.Businesses.Items).Slug);
    }

    [Fact]
    public async Task Search_ShouldBringProductsOnlyWithWords_AndOnlyOnItsFirstPage()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        await ProductAsync(fixture, cafe, "Café de olla", 30m);
        var handler = Handler(fixture);

        Assert.Empty((await handler.Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Products);
        Assert.Empty((await handler.Handle(new GetPublicDirectoryQuery(Search: "   "), CancellationToken.None)).Products);
        Assert.Empty((await handler.Handle(new GetPublicDirectoryQuery(Search: "cafe", PageNumber: 2), CancellationToken.None)).Products);
        Assert.Single((await handler.Handle(new GetPublicDirectoryQuery(Search: "cafe"), CancellationToken.None)).Products);
    }

    [Fact]
    public async Task Card_ShouldCarryTheCoverOfItsBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        await ProductAsync(fixture, cafe, "Café de olla", 30m);
        cafe.SetCoverImage("/uploads/covers/luna/portada");
        await BusinessAsync(fixture, "sin-portada", "Sin portada");
        await fixture.Context.SaveChangesAsync();

        var cards = (await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None)).Businesses.Items;

        Assert.Equal("/uploads/covers/luna/portada_md.webp", Assert.Single(cards, c => c.Slug == "cafe-luna").CoverImageUrl);
    }

    [Fact]
    public async Task Favorites_ShouldBeKeptOnce_TheLastOneFirst_WithTheirCardsWhileTheyShowInExplorar()
    {
        await using var fixture = SalesTestFixture.Create();
        var luna = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        luna.Update("Café Luna", "luna@demo.com", null, "/uploads/logos/luna/logo");
        // Out of Explorar, with its menu published: kept by its name, without a card.
        var hidden = await BusinessAsync(fixture, "cafe-oculto", "Café Oculto");
        (await SettingsAsync(fixture, hidden)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();
        var mariana = await AccountAsync(fixture, "mariana@demo.com");
        SignIn(fixture, mariana);

        Assert.True((await SaveAsync(fixture, "cafe-luna")).IsSuccess);
        Assert.True((await SaveAsync(fixture, " CAFE-OCULTO ")).IsSuccess);
        // Again: still one.
        Assert.True((await SaveAsync(fixture, "cafe-luna")).IsSuccess);
        await KeptAtAsync(fixture, mariana, luna, minutesAgo: 10);
        await KeptAtAsync(fixture, mariana, hidden, minutesAgo: 1);

        var favorites = await FavoritesAsync(fixture);

        Assert.Equal(["cafe-oculto", "cafe-luna"], favorites.Select(f => f.Slug));
        Assert.Null(favorites[0].Card);
        Assert.Equal("Café Oculto", favorites[0].Name);
        Assert.Equal("Café Luna", favorites[1].Card!.Name);
        Assert.Equal("/uploads/logos/luna/logo_sm.webp", favorites[1].LogoUrl);
        Assert.Equal(2, await fixture.Context.FavoriteBusinesses.CountAsync());
    }

    [Fact]
    public async Task Favorites_ShouldOnlyKeepAPublishedStore_ForACustomerAccount()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, "sin-menu", "Sin menú", mode: OnlineMenuMode.Off);
        await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));

        Assert.Equal(FavoriteBusinessRules.NotFoundMessage, (await SaveAsync(fixture, "sin-menu")).Error);
        Assert.Equal(FavoriteBusinessRules.NotFoundMessage, (await SaveAsync(fixture, "no-existe")).Error);

        // The team of a business, or no session, keeps none and lists none.
        SignIn(fixture, await AccountAsync(fixture, "dueno@demo.com", UserRole.TenantAdmin));
        Assert.Equal(FavoriteBusinessRules.CustomersOnlyMessage, (await SaveAsync(fixture, "cafe-luna")).Error);
        Assert.Empty(await FavoritesAsync(fixture));
        fixture.CurrentUser.UserId = null;
        Assert.Equal(FavoriteBusinessRules.CustomersOnlyMessage, (await RemoveAsync(fixture, "cafe-luna")).Error);
        Assert.Empty(await fixture.Context.FavoriteBusinesses.ToListAsync());
    }

    [Fact]
    public async Task Favorites_ShouldBeOnlyTheAccounts()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        var ana = await AccountAsync(fixture, "ana@demo.com");
        var beto = await AccountAsync(fixture, "beto@demo.com");

        SignIn(fixture, ana);
        Assert.True((await SaveAsync(fixture, "cafe-luna")).IsSuccess);
        SignIn(fixture, beto);
        Assert.Empty(await FavoritesAsync(fixture));
        // Taking it out of Beto's favorites leaves Ana's as they are.
        Assert.True((await RemoveAsync(fixture, "cafe-luna")).IsSuccess);
        SignIn(fixture, ana);
        Assert.Equal("cafe-luna", Assert.Single(await FavoritesAsync(fixture)).Slug);

        Assert.True((await RemoveAsync(fixture, "cafe-luna")).IsSuccess);
        Assert.Empty(await FavoritesAsync(fixture));
        // Again: nothing to take out.
        Assert.True((await RemoveAsync(fixture, "cafe-luna")).IsSuccess);
    }

    [Fact]
    public async Task Favorites_ShouldLeaveOutABusinessSuspendedOrWithoutItsMenu_UntilItIsBack_AndStillTakeItOut()
    {
        await using var fixture = SalesTestFixture.Create();
        var luna = await BusinessAsync(fixture, "cafe-luna", "Café Luna");
        var sol = await BusinessAsync(fixture, "cafe-sol", "Café Sol");
        SignIn(fixture, await AccountAsync(fixture, "mariana@demo.com"));
        await SaveAsync(fixture, "cafe-luna");
        await SaveAsync(fixture, "cafe-sol");

        luna.SetStatus(TenantStatus.Suspended);
        (await SettingsAsync(fixture, sol)).UpdateOnlineMenu(OnlineMenuMode.Off);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await FavoritesAsync(fixture));

        (await SettingsAsync(fixture, sol)).UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal("cafe-sol", Assert.Single(await FavoritesAsync(fixture)).Slug);

        Assert.True((await RemoveAsync(fixture, "cafe-luna")).IsSuccess);
        Assert.Equal([sol.Id], await fixture.Context.FavoriteBusinesses.Select(f => f.BusinessId).ToListAsync());
    }

    private static async Task<Tenant> BusinessAsync(
        SalesTestFixture fixture,
        string slug,
        string name,
        BusinessCategory category = BusinessCategory.Food,
        OnlineMenuMode mode = OnlineMenuMode.Orders,
        bool drawer = false)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(category, null);
        (await SettingsAsync(fixture, tenant)).UpdateOnlineMenu(mode);
        await fixture.Context.SaveChangesAsync();
        if (drawer)
            await fixture.OpenCashSessionAsync(tenant);
        // Explorar lists a business whose menu has a product: a search for «zz» finds none of these.
        await ProductAsync(fixture, tenant, "Producto de la casa zz", 10m);
        return tenant;
    }

    private static async Task<Product> ProductAsync(
        SalesTestFixture fixture,
        Tenant tenant,
        string name,
        decimal price,
        string? description = null,
        decimal? compareAtPrice = null,
        string? image = null,
        UnitType? unit = null)
    {
        var category = await fixture.Context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.IsActive);
        if (category is null)
        {
            category = new Category(tenant.Id, "General", null);
            fixture.Context.Categories.Add(category);
        }

        var product = new Product(tenant.Id, category.Id, name, description, price, "MXN", imageUrl: image, compareAtPrice: compareAtPrice, unit: unit);
        fixture.Context.Products.Add(product);
        await fixture.Context.SaveChangesAsync();
        return product;
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

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
        return account;
    }

    private static void SignIn(SalesTestFixture fixture, ApplicationUser account)
    {
        fixture.CurrentUser.UserId = account.Id.ToString();
        fixture.CurrentUser.Role = account.Role == UserRole.Customer ? "CUSTOMER" : "TENANT_ADMIN";
    }

    /// <summary>When the favorite was kept: the interceptor that stamps it does not run here.</summary>
    private static async Task KeptAtAsync(SalesTestFixture fixture, ApplicationUser account, Tenant business, int minutesAgo)
    {
        var favorite = await fixture.Context.FavoriteBusinesses.SingleAsync(f => f.UserId == account.Id && f.BusinessId == business.Id);
        favorite.CreatedAt = SalesTestFixture.Now.AddMinutes(-minutesAgo);
        await fixture.Context.SaveChangesAsync();
    }

    private static GetPublicDirectoryQueryHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);

    private static Task<PublicDirectoryDto> SearchAsync(SalesTestFixture fixture, string search, BusinessCategory? category = null)
        => Handler(fixture).Handle(new GetPublicDirectoryQuery(category, search), CancellationToken.None);

    private static Task<List<MyFavoriteBusinessDto>> FavoritesAsync(SalesTestFixture fixture)
        => new GetMyFavoritesQueryHandler(
                fixture.Context, fixture.CurrentUser, fixture.Clock, new PublicDirectoryCache(), NullLogger<GetMyFavoritesQueryHandler>.Instance)
            .Handle(new GetMyFavoritesQuery(), CancellationToken.None);

    private static Task<Common.Models.Result> SaveAsync(SalesTestFixture fixture, string slug)
        => new FavoriteBusinessCommandHandlers(fixture.Context, fixture.CurrentUser).Handle(new SaveMyFavoriteCommand(slug), CancellationToken.None);

    private static Task<Common.Models.Result> RemoveAsync(SalesTestFixture fixture, string slug)
        => new FavoriteBusinessCommandHandlers(fixture.Context, fixture.CurrentUser).Handle(new RemoveMyFavoriteCommand(slug), CancellationToken.None);
}
