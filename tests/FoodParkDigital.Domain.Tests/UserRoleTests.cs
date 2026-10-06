using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>A role left out grants nothing: zero is no role, not the platform admin.</summary>
public class UserRoleTests
{
    [Fact]
    public void DefaultRole_ShouldGrantNothing_EvenToTheOwnerOfABusiness()
    {
        UserRole omitted = default;

        Assert.Equal(UserRole.None, omitted);
        Assert.Equal(UserRole.None, TenantPermissionCatalog.EffectiveRole(omitted, TenantUserRole.Owner));
        Assert.Empty(TenantPermissionCatalog.GetDefaultCodes(omitted, TenantUserRole.Owner));
    }

    [Fact]
    public void Roles_ShouldKeepTheNumbersOfTheApi_AndThePlatformAdminAwayFromZero()
    {
        // Requests and responses carry these numbers: an employee's role keeps its value.
        Assert.Equal(1, (int)UserRole.TenantAdmin);
        Assert.Equal(2, (int)UserRole.Staff);
        Assert.Equal(3, (int)UserRole.Kitchen);
        Assert.Equal(4, (int)UserRole.Customer);
        Assert.Equal(9, (int)UserRole.SuperAdmin);
        Assert.Equal(0, (int)UserRole.None);
    }
}
