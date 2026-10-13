using System.Globalization;
using FoodParkDigital.Application.AiAgents;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A question about delivering on WhatsApp, for a business with delivery zones (Fase 22B): whether they reach the postal
/// code written and what they charge, a request to write one when there is none, and the texts of the business around it
/// as around the hours. A business without an active zone answers as before.
/// </summary>
public class AutoReplyDeliveryZonesTests
{
    private const string AppUrl = "https://app.tienditapp.mx";
    private const string MenuLink = "https://app.tienditapp.mx/m/demo";
    private const string Description = "Cocina casera en Puebla.";
    private const string Order = $"Mira el menú y haz tu pedido aquí: {MenuLink}";
    private const string Products = "Tacos $25, aguas $15.";
    private const string CoversCentro =
        "Sí llegamos al 72000: el envío cuesta $35.00. Pedido mínimo de $150.00. Envío gratis en pedidos desde $400.00.";

    // Monday at noon in Mexico City.
    private static readonly DateTimeOffset MondayNoon = new(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("¿Hacen envíos al 72000?")]
    [InlineData("72000")]
    [InlineData("C.P. 72000")]
    [InlineData("Mi código postal es el 72000")]
    [InlineData("¿Cuánto cuesta el envío a la colonia del CP 72000?")]
    // «mandan» and «colonia» also mean something else, so they count next to a postal code.
    [InlineData("¿Me mandan al 72000?")]
    [InlineData("Mi colonia es del 72000")]
    public async Task PostalCodeOfAZone_ShouldGetWhatItCharges_AndTheMenu(string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, message, null);

        Assert.Equal($"{CoversCentro}\n\n{Order}", reply);
    }

    [Fact]
    public async Task BusinessWithPricesBeforeIva_ShouldSayTheFeeIsPlusIva_AsTheOrderChargesIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture, pricesIncludeTax: false);
        await AddZoneAsync(fixture, tenant, "Centro", 35m, 150m, 400m, isActive: true, "72000");
        await AddZoneAsync(fixture, tenant, "Norte", 0m, 0m, null, isActive: true, "72100");

        var paid = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Envían al 72000?", null);
        var free = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Envían al 72100?", null);

        // The minimum and the free delivery are what the customer pays in products: only the fee gets «más IVA».
        Assert.StartsWith(
            "Sí llegamos al 72000: el envío cuesta $35.00 más IVA. Pedido mínimo de $150.00. Envío gratis en pedidos desde $400.00.", paid);
        Assert.StartsWith("Sí llegamos al 72100: el envío es gratis.", free);
    }

    [Fact]
    public async Task FreeDelivery_AndAZoneWithoutMinimum_ShouldSaySoAndNoMore()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Envían al 72100?", null);

        Assert.Equal($"Sí llegamos al 72100: el envío es gratis.\n\n{Order}", reply);
    }

    [Fact]
    public async Task PostalCodeOutOfTheZones_ShouldGetThatTheyDoNotDeliverThere()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);

        var outside = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Hacen envíos al 75000?", null);
        // A zone turned off reaches none of its postal codes.
        var inactive = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Hacen envíos al 72830?", null);

        Assert.Equal($"{AiAgentAutoReplyService.NotCoveredText("75000")}\n\n{Order}", outside);
        Assert.Equal($"{AiAgentAutoReplyService.NotCoveredText("72830")}\n\n{Order}", inactive);
    }

    [Theory]
    [InlineData("¿Tienen servicio a domicilio?")]
    [InlineData("¿Cuánto cobran de envío?")]
    [InlineData("¿Cuál es el costo de envío?")]
    [InlineData("¿Llegan a la colonia Roma?")]
    [InlineData("¿Envían a Cholula?")]
    [InlineData("¿Entregan en San Andrés?")]
    [InlineData("¿Qué zonas cubren?")]
    public async Task DeliveryQuestion_WithoutAPostalCode_ShouldAskForIt(string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture, products: Products);

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, message, null);

        // Without the products: «costo» or «servicio» in a question about the delivery does not ask for the menu.
        Assert.Equal($"{AiAgentAutoReplyService.AskForPostalCodeText}\n\n{Order}", reply);
    }

    [Theory]
    [InlineData("Te envío el comprobante")]
    [InlineData("Gracias por el envío")]
    [InlineData("¿Me mandan el número de cuenta?")]
    [InlineData("¿Me entregas factura?")]
    [InlineData("¿En qué colonia están?")]
    public async Task WordsThatAlsoMeanSomethingElse_WithoutAPostalCode_ShouldAnswerAsBefore(string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);
        var agent = await fixture.Context.AiAgents.SingleAsync();

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, message, null);

        Assert.Equal(agent.FallbackMessage, reply);
    }

    [Fact]
    public async Task DeliveryCost_AndThePricesOfTheMenu_ShouldGetBoth()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture, products: Products);

        var reply = await Replies(fixture).BuildReplyAsync(
            fixture.TenantService.TenantId, "¿Cuánto cuesta el envío y cuánto cuestan los tacos?", null);

        Assert.Equal($"{AiAgentAutoReplyService.AskForPostalCodeText}\n\n{Products}\n\n{Order}", reply);
    }

    [Fact]
    public async Task GreetingAndPostalCode_ShouldWelcomeFirst_AndAskForTheNameLast()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);
        var agent = await fixture.Context.AiAgents.SingleAsync();

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "Hola, ¿hacen envíos al 72000?", null);

        Assert.Equal($"{agent.WelcomeMessage}\n\n{CoversCentro}\n\n{Order}\n\n¿Me compartes tu nombre?", reply);
    }

    [Fact]
    public async Task HoursAndPostalCode_ShouldTellTheHoursFirst()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture, OpeningHours.Create(
            Enumerable.Range(1, 6).Select(day => new OpeningInterval((DayOfWeek)day, Time("09:00"), Time("20:00")))));

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿A qué hora abren y envían al 72000?", null);

        Assert.StartsWith("Ahora estamos abiertos: cerramos a las 20:00.\n\n", reply);
        Assert.Contains($"\n\n{CoversCentro}\n\n", reply);
        Assert.EndsWith(Order, reply);
    }

    [Fact]
    public async Task WhereAndPostalCode_ForAKitchenWithoutPremises_ShouldSayBoth_WithItsDescription()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessWithZonesAsync(fixture);
        var main = await fixture.DefaultBranchAsync();
        main.Update(main.Name, main.Code, new BranchContact(AddressLine1: "Privada Los Pinos 8"), BranchServiceMode.DeliveryOnly);
        await fixture.Context.SaveChangesAsync();

        var reply = await Replies(fixture).BuildReplyAsync(fixture.TenantService.TenantId, "¿Dónde están? ¿Hacen envíos al 72000?", null);

        Assert.Equal($"{AiAgentAutoReplyService.DeliveryOnlyText}\n\n{CoversCentro}\n\n{Description}\n\n{Order}", reply);
        Assert.DoesNotContain("Los Pinos", reply);
    }

    [Fact]
    public async Task WithoutActiveZones_ShouldAnswerAsBefore()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        var agent = await fixture.Context.AiAgents.SingleAsync();
        var withoutZones = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Hacen envíos al 72000?", null);

        await AddZoneAsync(fixture, tenant, "Centro", 35m, 0m, null, isActive: false, "72000");
        var withAnInactiveZone = await Replies(fixture).BuildReplyAsync(tenant.Id, "¿Hacen envíos al 72000?", null);

        Assert.Equal(agent.FallbackMessage, withoutZones);
        Assert.Equal(agent.FallbackMessage, withAnInactiveZone);
    }

    [Fact]
    public async Task TwoBusinesses_ShouldEachAnswerWithTheirOwnZones()
    {
        await using var fixture = SalesTestFixture.Create();
        var kitchen = await BusinessWithZonesAsync(fixture);
        var neighbor = await BusinessAsync(fixture, slug: "fonda-vecina");
        await AddZoneAsync(fixture, neighbor, "Todo Puebla", 50m, 0m, null, isActive: true, "72000");

        var theirs = await Replies(fixture).BuildReplyAsync(neighbor.Id, "¿Hacen envíos al 72000?", null);
        fixture.TenantService.SetTenant(kitchen.Id);
        var own = await Replies(fixture).BuildReplyAsync(kitchen.Id, "¿Hacen envíos al 72000?", null);

        Assert.StartsWith("Sí llegamos al 72000: el envío cuesta $50.00.\n\n", theirs);
        Assert.StartsWith($"{CoversCentro}\n\n", own);
    }

    /// <summary>
    /// A business with an active zone «Centro» (72000 and 72005: $35, minimum $150, free from $400), an active zone with
    /// free delivery (72100) and an inactive one (72830).
    /// </summary>
    private static async Task<Tenant> BusinessWithZonesAsync(SalesTestFixture fixture, OpeningHours? weekly = null, string? products = null)
    {
        var tenant = await BusinessAsync(fixture, weekly, products: products);
        await AddZoneAsync(fixture, tenant, "Centro", 35m, 150m, 400m, isActive: true, "72000", "72005");
        await AddZoneAsync(fixture, tenant, "Norte", 0m, 0m, null, isActive: true, "72100");
        await AddZoneAsync(fixture, tenant, "Angelópolis", 60m, 0m, null, isActive: false, "72830");
        return tenant;
    }

    private static async Task AddZoneAsync(
        SalesTestFixture fixture, Tenant tenant, string name, decimal fee, decimal minimum, decimal? freeFrom, bool isActive,
        params string[] postalCodes)
    {
        fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields(name, fee, minimum, freeFrom, postalCodes, isActive)));
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>
    /// A business with its online menu taking orders and an active agent with a description; its prices include IVA, as a
    /// new business's do.
    /// </summary>
    private static async Task<Tenant> BusinessAsync(
        SalesTestFixture fixture, OpeningHours? weekly = null, string slug = "demo", string? products = null, bool pricesIncludeTax = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: pricesIncludeTax, slug: slug);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.SetOpeningHours(weekly);
        var agent = new AiAgent(tenant.Id, "Respuestas", "Atención");
        agent.UpdateGeneral("Respuestas", "Atención", "Amable", "Español", "Amigable", tenant.Name, Description, products);
        agent.UpdateAiConfiguration("keywords", 0.4m, 800, false, strictMode: true);
        fixture.Context.AiAgents.Add(agent);
        await fixture.Context.SaveChangesAsync();
        fixture.Clock.UtcNow = MondayNoon;
        return tenant;
    }

    private static TimeOnly Time(string value) => TimeOnly.Parse(value, CultureInfo.InvariantCulture);

    private static AiAgentAutoReplyService Replies(SalesTestFixture fixture)
        => new(fixture.Context, new Links(AppUrl), fixture.Clock);

    private sealed record Links(string? BaseUrl) : IAppLinks;
}
