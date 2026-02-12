using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
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
        string[] roles = ["SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN"];
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
                TenantId = systemTenant.Id,
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
            }
        }
    }
}
