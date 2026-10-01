using FoodParkDigital.Application.Identity;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests.Support;

internal sealed record GuestOrder(Guid TenantId, Guid TemporaryCustomerId, Guid OrderId);

internal sealed record UnconfirmedCustomer(Guid UserId, string PhoneCode);

internal static class CustomerSeed
{
    /// <summary>A tenant with a guest (temporary) customer and one order placed with <paramref name="phone"/>.</summary>
    public static Task<GuestOrder> GuestOrderAsync(SqliteTestHost host, string tenantSlug, string phone)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();

            var tenant = new Tenant($"Tenant {tenantSlug}", tenantSlug, BusinessType.Food, $"{tenantSlug}@demo.com");
            var temp = new TemporaryCustomer(tenant.Id, phone, "Cliente Invitado");
            var order = new Order(
                tenantId: tenant.Id,
                customerUserId: null,
                temporaryCustomerId: temp.Id,
                createdByBusiness: false,
                orderNumber: "ORD-000001",
                businessType: tenant.BusinessType,
                orderType: OrderType.Retail,
                customerName: "Cliente Invitado",
                customerPhone: phone,
                customerNotes: null,
                tableNumber: null);

            db.Tenants.Add(tenant);
            db.TemporaryCustomers.Add(temp);
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            return new GuestOrder(tenant.Id, temp.Id, order.Id);
        });

    /// <summary>
    /// A registered customer whose phone is not confirmed yet, plus the SMS code Identity would send.
    /// Requires the CUSTOMER role.
    /// </summary>
    public static Task<UnconfirmedCustomer> UnconfirmedCustomerAsync(SqliteTestHost host, string email, string phone)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = "Ana",
                LastName = "Cliente",
                PhoneNumber = phone,
                Role = UserRole.Customer,
                IsActive = true
            };

            EnsureSucceeded(await userManager.CreateAsync(user, "Secreto1"));
            EnsureSucceeded(await userManager.AddToRoleAsync(user, "CUSTOMER"));

            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CustomerProfiles.Add(new CustomerProfile(user.Id, phone, "Ana Cliente", "Ana", "Cliente"));
            await db.SaveChangesAsync();

            var code = await userManager.GenerateChangePhoneNumberTokenAsync(user, phone);
            return new UnconfirmedCustomer(user.Id, code);
        });

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(result.ToErrorMessage());
    }
}
