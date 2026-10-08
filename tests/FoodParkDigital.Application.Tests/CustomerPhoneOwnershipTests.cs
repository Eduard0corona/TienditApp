using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A customer profile's phone links orders to its account only after that account confirmed the
/// number by SMS. Registering with a number proves nothing, so it neither reserves the number nor
/// evicts anyone; whoever confirms it takes it, unless another account confirmed it first.
/// </summary>
public class CustomerPhoneOwnershipTests
{
    // The number at stake, as typed in forms and as stored in profiles.
    private const string OwnerPhoneInput = "55 1234 5678";
    private const string OwnerPhone = "+525512345678";

    // A number the other account does control.
    private const string OtherPhoneInput = "55 8765 4321";
    private const string OtherPhone = "+525587654321";

    [Fact]
    public async Task CreateOrder_ShouldNotLinkOrdersOfAnyTenant_ToAnAccountThatNeverConfirmedThePhone()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var storeA = await SeedStoreAsync(host, "tenant-a");
        var storeB = await SeedStoreAsync(host, "tenant-b");
        var attackerId = await RegisterUserAsync(host, "atacante@demo.com", OwnerPhoneInput);

        var orderA = await CreateOrderAsync(host, storeA, OwnerPhoneInput);
        var orderB = await CreateOrderAsync(host, storeB, OwnerPhoneInput);

        Assert.Null(orderA.CustomerUserId);
        Assert.Null(orderB.CustomerUserId);

        // Each tenant keeps its own guest customer until someone confirms the number.
        var stored = await host.QueryAsync(async db => new
        {
            AttackerOrders = await db.Orders.IgnoreQueryFilters().CountAsync(o => o.CustomerUserId == attackerId),
            Temps = await db.TemporaryCustomers.IgnoreQueryFilters().ToDictionaryAsync(t => t.Id)
        });
        Assert.Equal(0, stored.AttackerOrders);
        Assert.Equal(2, stored.Temps.Count);
        Assert.Equal(storeA.TenantId, stored.Temps[orderA.TemporaryCustomerId!.Value].TenantId);
        Assert.Equal(storeB.TenantId, stored.Temps[orderB.TemporaryCustomerId!.Value].TenantId);
        Assert.All(stored.Temps.Values, temp =>
        {
            Assert.Equal(OwnerPhone, temp.PhoneNormalized);
            Assert.Equal(TemporaryCustomerStatus.PendingInvited, temp.Status);
            Assert.Null(temp.ClaimedUserId);
        });
    }

    [Fact]
    public async Task CreateOrder_ShouldLinkOrdersOfEveryTenant_ToTheAccountThatConfirmedThePhone()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var storeA = await SeedStoreAsync(host, "tenant-a");
        var storeB = await SeedStoreAsync(host, "tenant-b");
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);
        await ConfirmPhoneAsync(host, ownerId);

        var orderA = await CreateOrderAsync(host, storeA, OwnerPhoneInput);
        var orderB = await CreateOrderAsync(host, storeB, OwnerPhoneInput);

        Assert.All(new[] { orderA, orderB }, order =>
        {
            Assert.Equal(ownerId, order.CustomerUserId);
            Assert.Null(order.TemporaryCustomerId);
        });
        Assert.Empty(await host.QueryAsync(db => db.TemporaryCustomers.IgnoreQueryFilters().ToListAsync()));
    }

    [Fact]
    public async Task CreateOrder_ShouldNotLinkOrder_WhenTheAccountConfirmedAnotherPhoneThanItsProfile()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var store = await SeedStoreAsync(host, "tenant-a");
        var attackerId = await RegisterUserAsync(host, "atacante@demo.com", OwnerPhoneInput);

        // The account moves to a number it controls and confirms that one. Before profiles followed
        // confirmations this left PhoneNumberConfirmed set while the profile kept the victim's number,
        // so existing data can still be in this state.
        await ChangePhoneAsync(host, attackerId, OtherPhone);
        await UpdateUserAsync(host, attackerId, user => user.PhoneNumberConfirmed = true);

        var order = await CreateOrderAsync(host, store, OwnerPhoneInput);

        Assert.Null(order.CustomerUserId);
        Assert.NotNull(order.TemporaryCustomerId);
    }

    [Fact]
    public async Task RealOwner_ShouldRegisterAndTakeThePhone_WhenAnUnverifiedAccountRegisteredItFirst()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var storeA = await SeedStoreAsync(host, "tenant-a");
        var storeB = await SeedStoreAsync(host, "tenant-b");
        var attackerId = await RegisterUserAsync(host, "atacante@demo.com", OwnerPhoneInput);
        var guestOrderA = await CreateOrderAsync(host, storeA, OwnerPhoneInput);
        var guestOrderB = await CreateOrderAsync(host, storeB, OwnerPhoneInput);

        // The unverified profile no longer locks the owner out, but nothing moves without an SMS proof.
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);

        var beforeConfirming = Assert.Single(await ProfilePhonesAsync(host));
        Assert.Equal(attackerId, beforeConfirming.Key);

        await ConfirmPhoneAsync(host, ownerId);

        var afterConfirming = Assert.Single(await ProfilePhonesAsync(host));
        Assert.Equal(ownerId, afterConfirming.Key);
        Assert.Equal(OwnerPhone, afterConfirming.Value);

        // The attacker keeps the account, just not the number.
        var attacker = await host.QueryAsync(db => db.Users.SingleAsync(u => u.Id == attackerId));
        Assert.False(attacker.PhoneNumberConfirmed);

        // Guest orders of both tenants were claimed by the owner and new ones link directly.
        var newOrderA = await CreateOrderAsync(host, storeA, OwnerPhoneInput);
        Assert.Equal(ownerId, newOrderA.CustomerUserId);

        var orders = await host.QueryAsync(db => db.Orders.IgnoreQueryFilters().ToDictionaryAsync(o => o.Id));
        Assert.Equal(3, orders.Count);
        foreach (var orderId in new[] { guestOrderA.Id, guestOrderB.Id, newOrderA.Id })
        {
            Assert.Equal(ownerId, orders[orderId].CustomerUserId);
            Assert.Null(orders[orderId].TemporaryCustomerId);
        }
    }

    [Fact]
    public async Task RegisterCustomer_ShouldNotEvictAPendingProfile_WhenAnotherAccountRegistersTheSamePhone()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);

        var attackerId = await RegisterUserAsync(host, "atacante@demo.com", OwnerPhoneInput);

        var pending = Assert.Single(await ProfilePhonesAsync(host));
        Assert.Equal(ownerId, pending.Key);

        await ConfirmPhoneAsync(host, ownerId);

        var confirmed = Assert.Single(await ProfilePhonesAsync(host));
        Assert.Equal(ownerId, confirmed.Key);
        Assert.Equal(OwnerPhone, confirmed.Value);
        Assert.True(await host.QueryAsync(db => db.Users.AnyAsync(u => u.Id == attackerId)));
    }

    [Fact]
    public async Task RegisterCustomer_ShouldReject_WhenAnotherAccountConfirmedThePhone()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);
        await ConfirmPhoneAsync(host, ownerId);

        var result = await RegisterAsync(host, "atacante@demo.com", OwnerPhoneInput);

        Assert.True(result.IsFailure);
        Assert.Equal("Ya existe una cuenta de cliente con este teléfono.", result.Error);
        Assert.Equal(ownerId, Assert.Single(await host.QueryAsync(db => db.Users.Select(u => u.Id).ToListAsync())));
        Assert.Equal(ownerId, Assert.Single(await ProfilePhonesAsync(host)).Key);
    }

    [Fact]
    public async Task ConfirmPhone_ShouldNotTakeThePhoneFromAnAccountThatConfirmedItFirst()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var store = await SeedStoreAsync(host, "tenant-a");
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);
        await ConfirmPhoneAsync(host, ownerId);

        // A second account also proves the number, e.g. a shared or recycled line.
        var otherId = await RegisterUserAsync(host, "otra@demo.com", OtherPhoneInput);
        await ChangePhoneAsync(host, otherId, OwnerPhone);
        await ConfirmPhoneAsync(host, otherId);

        var profiles = await ProfilePhonesAsync(host);
        Assert.Equal(2, profiles.Count);
        Assert.Equal(OwnerPhone, profiles[ownerId]);
        Assert.Equal(OtherPhone, profiles[otherId]);

        var order = await CreateOrderAsync(host, store, OwnerPhoneInput);
        Assert.Equal(ownerId, order.CustomerUserId);
    }

    [Fact]
    public async Task ConfirmPhone_ShouldMoveTheProfileToTheConfirmedPhone_AndReleaseThePreviousOne()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var attackerId = await RegisterUserAsync(host, "atacante@demo.com", OwnerPhoneInput);

        await ChangePhoneAsync(host, attackerId, OtherPhone);
        await ConfirmPhoneAsync(host, attackerId);

        var moved = Assert.Single(await ProfilePhonesAsync(host));
        Assert.Equal(attackerId, moved.Key);
        Assert.Equal(OtherPhone, moved.Value);

        // The number the attacker never proved is free again for its owner.
        var ownerId = await RegisterUserAsync(host, "duena@demo.com", OwnerPhoneInput);

        var profiles = await ProfilePhonesAsync(host);
        Assert.Equal(2, profiles.Count);
        Assert.Equal(OwnerPhone, profiles[ownerId]);
        Assert.Equal(OtherPhone, profiles[attackerId]);
    }

    private sealed record Store(Guid TenantId, Guid ProductId);

    private static Task<Store> SeedStoreAsync(SqliteTestHost host, string tenantSlug)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();

            var tenant = new Tenant($"Tenant {tenantSlug}", tenantSlug, BusinessType.Food, $"{tenantSlug}@demo.com");
            var category = new Category(tenant.Id, "General", null);
            var product = new Product(tenant.Id, category.Id, "Producto Demo", null, 100m, "MXN");

            db.Tenants.Add(tenant);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();

            return new Store(tenant.Id, product.Id);
        });

    private static async Task<OrderDto> CreateOrderAsync(SqliteTestHost host, Store store, string customerPhone)
    {
        host.SetTenant(store.TenantId);
        var result = await host.HandleAsync<CreateOrderCommandHandler, Result<OrderDto>>(handler => handler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = customerPhone,
                CustomerName = "Cliente Mostrador",
                Items = [new CreateOrderItemDto { ProductId = store.ProductId, Quantity = 1 }]
            },
            CancellationToken.None));

        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private static Task<Result<Guid>> RegisterAsync(SqliteTestHost host, string email, string phoneNumber)
    {
        host.SetTenant(Guid.Empty);
        return host.HandleAsync<RegisterCustomerCommandHandler, Result<Guid>>(handler => handler.Handle(
            new RegisterCustomerCommand
            {
                Email = email,
                Password = "Secreto1",
                FirstName = "Cliente",
                LastName = "Demo",
                PhoneNumber = phoneNumber
            },
            CancellationToken.None));
    }

    private static async Task<Guid> RegisterUserAsync(SqliteTestHost host, string email, string phoneNumber)
    {
        var result = await RegisterAsync(host, email, phoneNumber);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    /// <summary>Confirms the account's current phone with the SMS code Identity sends to it.</summary>
    private static async Task ConfirmPhoneAsync(SqliteTestHost host, Guid userId)
    {
        host.SetTenant(Guid.Empty);
        var code = await host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString())
                ?? throw new InvalidOperationException("User not found.");
            return await userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber!);
        });

        var result = await host.HandleAsync<ConfirmPhoneCommandHandler, Result>(handler => handler.Handle(
            new ConfirmPhoneCommand { UserId = userId.ToString(), Code = code },
            CancellationToken.None));

        Assert.True(result.IsSuccess, result.Error);
    }

    /// <summary>
    /// Changes the number of an account that has not confirmed it yet, like the confirmation screen does (with the
    /// password). The new number stays unconfirmed until its code is entered.
    /// </summary>
    private static async Task ChangePhoneAsync(SqliteTestHost host, Guid userId, string phoneNumber)
    {
        host.SetTenant(Guid.Empty);
        var result = await host.HandleAsync<ChangeUnconfirmedPhoneCommandHandler, Result>(handler => handler.Handle(
            new ChangeUnconfirmedPhoneCommand { UserId = userId, Password = "Secreto1", PhoneNumber = phoneNumber },
            CancellationToken.None));

        Assert.True(result.IsSuccess, result.Error);
    }

    private static Task UpdateUserAsync(SqliteTestHost host, Guid userId, Action<ApplicationUser> change)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString())
                ?? throw new InvalidOperationException("User not found.");
            change(user);

            var result = await userManager.UpdateAsync(user);
            Assert.True(result.Succeeded, result.ToErrorMessage());
        });

    /// <summary>Phone held by each customer profile, keyed by account.</summary>
    private static Task<Dictionary<Guid, string>> ProfilePhonesAsync(SqliteTestHost host)
        => host.QueryAsync(db => db.CustomerProfiles.ToDictionaryAsync(p => p.UserId, p => p.PhoneNormalized));
}
