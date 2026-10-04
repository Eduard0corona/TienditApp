using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Persistence.Seeds;

public static class SeedData
{
    public const string DefaultSuperAdminEmail = "admin@foodparkdigital.com";

    public static async Task SeedAsync(ApplicationDbContext context, IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var configuration = services.GetService<IConfiguration>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(SeedData).FullName!);

        // Seed roles
        string[] roles = ["SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN", "CUSTOMER"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new ApplicationRole(role));
        }

        // Ensure system tenant exists
        var systemTenant = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == "system");

        if (systemTenant is null)
        {
            systemTenant = new Tenant(
                name: "System",
                slug: "system",
                businessType: BusinessType.Food,
                contactEmail: "admin@foodparkdigital.com");

            context.Tenants.Add(systemTenant);
            await context.SaveChangesAsync();
        }

        // Ensure super admin user exists. The password only comes from configuration
        // (user-secrets or environment variables); without it the account is not created.
        var adminEmail = configuration?["Seed:SuperAdminEmail"] is { Length: > 0 } configuredEmail
            ? configuredEmail.Trim()
            : DefaultSuperAdminEmail;
        var adminPassword = configuration?["Seed:SuperAdminPassword"];

        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null && string.IsNullOrWhiteSpace(adminPassword))
        {
            logger?.LogWarning(
                "Seed: no se creó el SUPER_ADMIN inicial porque Seed:SuperAdminPassword no está configurado.");
        }
        else if (admin is null)
        {
            admin = new ApplicationUser
            {
                DefaultTenantId = systemTenant.Id,
                UserName = adminEmail,
                Email = adminEmail,
                FirstName = "System",
                LastName = "Admin",
                Role = UserRole.SuperAdmin,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, adminPassword!);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Could not seed super admin user: {errors}");
            }
        }

        if (admin is not null)
        {
            if (!await userManager.IsInRoleAsync(admin, "SUPER_ADMIN"))
                await userManager.AddToRoleAsync(admin, "SUPER_ADMIN");

            var hasOwnerMembership = await context.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == systemTenant.Id && x.UserId == admin.Id);

            if (!hasOwnerMembership)
            {
                context.TenantUsers.Add(new TenantUser(
                    systemTenant.Id,
                    admin.Id,
                    TenantUserRole.Owner,
                    TenantUserStatus.Active));
                await context.SaveChangesAsync();
            }
        }

        // Ensure every tenant has "Publico en general" customer
        var tenants = await context.Tenants
            .IgnoreQueryFilters()
            .Select(t => new { t.Id, t.BusinessType })
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            var tenantSettings = await context.TenantSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id);

            var defaultCapabilities = TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType);

            if (tenantSettings is null)
            {
                context.TenantSettings.Add(new TenantSettings(
                    tenant.Id,
                    defaultCapabilities,
                    pricesIncludeTax: true));
            }
            else if (tenantSettings.Capabilities == 0)
            {
                tenantSettings.UpdateCapabilities(defaultCapabilities);
            }

            var exists = await context.Customers
                .IgnoreQueryFilters()
                .AnyAsync(c => c.TenantId == tenant.Id && c.IsPublicGeneral);

            if (!exists)
            {
                context.Customers.Add(new Customer(
                    tenantId: tenant.Id,
                    name: "Publico en general",
                    phone: null,
                    email: null,
                    isPublicGeneral: true));
            }

            var hasDefaultLocation = await context.InventoryLocations
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == tenant.Id && x.IsDefault);

            if (!hasDefaultLocation)
            {
                context.InventoryLocations.Add(new InventoryLocation(
                    tenantId: tenant.Id,
                    name: "Almacen principal",
                    code: $"MAIN-{tenant.Id.ToString()[..8]}",
                    isDefault: true));
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
