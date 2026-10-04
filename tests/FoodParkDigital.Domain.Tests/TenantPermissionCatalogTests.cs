using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

public class TenantPermissionCatalogTests
{
    [Fact]
    public void NormalizeCodes_ShouldIgnoreInvalidAndDuplicateValues()
    {
        var codes = TenantPermissionCatalog.NormalizeCodes(
        [
            "ManageOrders",
            "manageorders",
            "InvalidPermission",
            "",
            "   ",
            "ManageInventory"
        ]);

        Assert.Equal(2, codes.Count);
        Assert.Contains("ManageOrders", codes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ManageInventory", codes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDefaultCodes_ForAdminRole_ShouldReturnAllCodes()
    {
        var adminCodes = TenantPermissionCatalog.GetDefaultCodes(UserRole.TenantAdmin, TenantUserRole.Admin);
        var allCodes = TenantPermissionCatalog.GetAllCodes();

        Assert.Equal(allCodes.Count, adminCodes.Count);
        Assert.All(allCodes, code => Assert.Contains(code, adminCodes));
    }

    [Theory]
    [InlineData(UserRole.TenantAdmin, TenantUserRole.Owner, UserRole.TenantAdmin)]
    [InlineData(UserRole.Staff, TenantUserRole.Admin, UserRole.TenantAdmin)]
    [InlineData(UserRole.Kitchen, TenantUserRole.Admin, UserRole.TenantAdmin)]
    [InlineData(UserRole.TenantAdmin, TenantUserRole.Staff, UserRole.Staff)] // Owns another business.
    [InlineData(UserRole.Kitchen, TenantUserRole.Staff, UserRole.Kitchen)]
    [InlineData(UserRole.Staff, TenantUserRole.Staff, UserRole.Staff)]
    [InlineData(UserRole.SuperAdmin, TenantUserRole.Staff, UserRole.SuperAdmin)]
    [InlineData(UserRole.Customer, TenantUserRole.Admin, UserRole.Customer)]
    public void EffectiveRole_ShouldComeFromTheMembershipInThatBusiness(
        UserRole accountRole, TenantUserRole membershipRole, UserRole expected)
    {
        Assert.Equal(expected, TenantPermissionCatalog.EffectiveRole(accountRole, membershipRole));
    }

    [Fact]
    public void GetDefaultCodes_ForTheOwnerOfAnotherBusiness_ShouldBeTheStaffOnesWhereTheyAreStaff()
    {
        var codes = TenantPermissionCatalog.GetDefaultCodes(UserRole.TenantAdmin, TenantUserRole.Staff);

        Assert.Equal(TenantPermissionCatalog.GetDefaultCodes(UserRole.Staff, TenantUserRole.Staff), codes);
        Assert.DoesNotContain("ManageEmployees", codes);
    }

    [Fact]
    public void ResolveEffectiveCodes_ShouldUseExplicitCodesWhenPresent()
    {
        var resolved = TenantPermissionCatalog.ResolveEffectiveCodes(
            "ManageOrders,ManageCash",
            UserRole.Staff,
            TenantUserRole.Staff);

        Assert.Equal(2, resolved.Count);
        Assert.Contains("ManageOrders", resolved);
        Assert.Contains("ManageCash", resolved);
        Assert.DoesNotContain("ManageEmployees", resolved);
    }

    [Fact]
    public void ResolveEffectiveCodes_ForKitchenWithoutExplicitCodes_ShouldUseKitchenDefaults()
    {
        var resolved = TenantPermissionCatalog.ResolveEffectiveCodes(
            null,
            UserRole.Kitchen,
            TenantUserRole.Staff);

        Assert.Contains("ManageOrders", resolved);
        Assert.Contains("ManageServiceBoard", resolved);
        Assert.DoesNotContain("ManageInventory", resolved);
    }
}
