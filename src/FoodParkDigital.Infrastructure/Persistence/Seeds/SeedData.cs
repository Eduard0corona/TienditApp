using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Persistence.Seeds;

public static class SeedData
{
    public static async Task SeedAsync(ApplicationDbContext context, IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        // Seed roles
        string[] roles = ["SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN", "CUSTOMER"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new ApplicationRole(role));
        }

        // Seed system tenant
        if (!await context.Tenants.IgnoreQueryFilters().AnyAsync())
        {
            var systemTenant = new Tenant(
                name: "System",
                slug: "system",
                businessType: BusinessType.Food,
                contactEmail: "admin@foodparkdigital.com");

            context.Tenants.Add(systemTenant);
            await context.SaveChangesAsync();

            // Seed super admin user
            var admin = new ApplicationUser
            {
                DefaultTenantId = systemTenant.Id,
                UserName = "admin@foodparkdigital.com",
                Email = "admin@foodparkdigital.com",
                FirstName = "System",
                LastName = "Admin",
                Role = UserRole.SuperAdmin,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, "Admin123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "SUPER_ADMIN");
                context.TenantUsers.Add(new TenantUser(
                    systemTenant.Id,
                    admin.Id,
                    TenantUserRole.Owner,
                    TenantUserStatus.Active));
                await context.SaveChangesAsync();
            }
        }

        // Ensure every tenant has "Publico en general" customer
        var tenantIds = await context.Tenants
            .IgnoreQueryFilters()
            .Select(t => t.Id)
            .ToListAsync();

        foreach (var tenantId in tenantIds)
        {
            var exists = await context.Customers
                .IgnoreQueryFilters()
                .AnyAsync(c => c.TenantId == tenantId && c.IsPublicGeneral);

            if (!exists)
            {
                context.Customers.Add(new Customer(
                    tenantId: tenantId,
                    name: "Publico en general",
                    phone: null,
                    email: null,
                    isPublicGeneral: true));
            }
        }

        // Backfill memberships for users with a default tenant
        var usersWithDefaultTenant = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.DefaultTenantId.HasValue)
            .ToListAsync();

        foreach (var user in usersWithDefaultTenant)
        {
            var tenantId = user.DefaultTenantId!.Value;
            var hasMembership = await context.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id);

            if (hasMembership)
                continue;

            var role = user.Role == UserRole.TenantAdmin
                ? TenantUserRole.Admin
                : TenantUserRole.Staff;

            context.TenantUsers.Add(new TenantUser(tenantId, user.Id, role, TenantUserStatus.Active));
        }

        await context.SaveChangesAsync();
    }
}
