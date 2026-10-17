using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A customer record keeps one form of the phone, notes and delivery addresses, and the chat finds it by the number
/// WhatsApp reports. A customer account sees its own orders in every business.
/// </summary>
public class CustomerRecordTests
{
    [Fact]
    public async Task Phone_ShouldBeNormalized_AndTheSameNumberRejectedInAnyForm()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        // A record saved before phones were normalized.
        fixture.Context.Customers.Add(new Customer(fixture.TenantService.TenantId, "Ana", "5598765432", null));
        await fixture.Context.SaveChangesAsync();

        var created = await CreateAsync(fixture, "Luis", "55 1234 5678");
        var sameAsWhatsApp = await CreateAsync(fixture, "Luis WhatsApp", "+5215512345678");
        var sameAsLegacy = await CreateAsync(fixture, "Ana otra vez", "+525598765432");
        var invalid = await CreateAsync(fixture, "Nadie", "12345");

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal("+525512345678", created.Value!.Phone);
        Assert.Equal("Ya existe un cliente con ese teléfono.", sameAsWhatsApp.Error);
        Assert.Equal("Ya existe un cliente con ese teléfono.", sameAsLegacy.Error);
        Assert.Equal(CustomerPhonesMessage, invalid.Error);
    }

    [Fact]
    public async Task Addresses_ShouldKeepExactlyOneDefault()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var customer = (await CreateAsync(fixture, "Luis", "5512345678")).Value!;
        var handlers = new CustomerAddressCommandHandlers(fixture.Context);

        var home = await handlers.Handle(new AddCustomerAddressCommand { CustomerId = customer.Id, Label = "Casa", AddressLine = "Av. Juárez 123, Centro" }, CancellationToken.None);
        var office = await handlers.Handle(new AddCustomerAddressCommand { CustomerId = customer.Id, Label = "Oficina", AddressLine = "Reforma 500, piso 3", References = "Torre azul", IsDefault = true }, CancellationToken.None);
        var officeId = office.Value!.Addresses.Single(a => a.Label == "Oficina").Id;
        var afterRemoving = await handlers.Handle(new RemoveCustomerAddressCommand(customer.Id, officeId), CancellationToken.None);

        Assert.True(home.Value!.Addresses.Single().IsDefault);
        Assert.Equal(["Oficina", "Casa"], office.Value.Addresses.Select(a => a.Label));
        Assert.Equal([true, false], office.Value.Addresses.Select(a => a.IsDefault));
        var remaining = Assert.Single(afterRemoving.Value!.Addresses);
        Assert.Equal("Casa", remaining.Label);
        Assert.True(remaining.IsDefault);
    }

    [Fact]
    public async Task Card_FromTheChat_ShouldShowTheNotesAndAddressesOfTheCustomerWithThatNumber()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var customer = (await CreateAsync(fixture, "Luis", "5512345678")).Value!;
        await new UpdateCustomerNotesCommandHandler(fixture.Context)
            .Handle(new UpdateCustomerNotesCommand(customer.Id, "Sin cebolla. Paga con tarjeta."), CancellationToken.None);
        await new CustomerAddressCommandHandlers(fixture.Context)
            .Handle(new AddCustomerAddressCommand { CustomerId = customer.Id, AddressLine = "Av. Juárez 123" }, CancellationToken.None);

        var card = await new GetCustomerCardQueryHandler(fixture.Context, new PhoneNormalizationService(), fixture.TenantService, fixture.Clock)
            .Handle(new GetCustomerCardQuery { Phone = "+5215512345678" }, CancellationToken.None);

        Assert.True(card.IsSuccess, card.Error);
        Assert.Equal(customer.Id, card.Value!.CustomerId);
        Assert.Equal("Sin cebolla. Paga con tarjeta.", card.Value.Notes);
        Assert.Equal("Av. Juárez 123", Assert.Single(card.Value.Addresses).AddressLine);
    }

    [Fact]
    public async Task CustomerOfAnotherBusiness_ShouldNotBeChanged()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var foreign = (await CreateAsync(fixture, "Ajeno", "5511112222")).Value!;
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "propio");

        var notes = await new UpdateCustomerNotesCommandHandler(fixture.Context)
            .Handle(new UpdateCustomerNotesCommand(foreign.Id, "nota"), CancellationToken.None);
        var address = await new CustomerAddressCommandHandlers(fixture.Context)
            .Handle(new AddCustomerAddressCommand { CustomerId = foreign.Id, AddressLine = "Calle 1" }, CancellationToken.None);

        Assert.Equal("El cliente no existe.", notes.Error);
        Assert.Equal("El cliente no existe.", address.Error);
        Assert.Empty(await fixture.Context.CustomerAddresses.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CustomerAccount_ShouldSeeItsOwnOrdersInEveryBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var cafe = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe");
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == cafe.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        var me = Guid.NewGuid();
        await AddOrderAsync(fixture, cafe, me, "ORD-000001", SalesTestFixture.Now.AddDays(-2));
        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "tienda");
        await AddOrderAsync(fixture, shop, me, "ORD-000001", SalesTestFixture.Now.AddDays(-1));
        await AddOrderAsync(fixture, shop, Guid.NewGuid(), "ORD-000002", SalesTestFixture.Now);
        fixture.CurrentUser.UserId = me.ToString();

        var mine = await new GetMyOrdersQueryHandler(fixture.Context, fixture.CurrentUser, fixture.Clock)
            .Handle(new GetMyOrdersQuery(), CancellationToken.None);

        Assert.Equal(["Negocio tienda", "Negocio cafe"], mine.Items.Select(o => o.BusinessName));
        Assert.Equal([false, true], mine.Items.Select(o => o.CanOrderAgain));
        Assert.Equal("Taco", mine.Items[1].Items.Single().ProductName);
    }

    private const string CustomerPhonesMessage = "Escribe el teléfono a 10 dígitos o con lada internacional (+52…).";

    private static Task<Application.Common.Models.Result<Application.Customers.DTOs.CustomerDto>> CreateAsync(SalesTestFixture fixture, string name, string phone)
        => new CreateCustomerCommandHandler(fixture.Context, fixture.TenantService, new PhoneNormalizationService())
            .Handle(new CreateCustomerCommand { Name = name, Phone = phone }, CancellationToken.None);

    private static async Task AddOrderAsync(SalesTestFixture fixture, Tenant tenant, Guid customerUserId, string number, DateTimeOffset createdAt)
    {
        var order = new Order(tenant.Id, customerUserId, null, false, number, tenant.BusinessType, OrderType.Retail, "Luis", "+525512345678", null, null);
        order.AddItem(Guid.NewGuid(), "Taco", 1, 25m);
        order.CreatedAt = createdAt;
        fixture.TenantService.SetTenant(tenant.Id);
        fixture.Context.Orders.Add(order);
        await fixture.Context.SaveChangesAsync();
    }
}
