using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The cover of the online menu (Fase 24E, ADR 0026).</summary>
public class MenuCoverTests
{
    [Fact]
    public void SetCoverImage_ShouldKeepTheStoredVariants_AndRemoveThemWithNothing()
    {
        var tenant = new Tenant("Café Luna", "cafe-luna", BusinessType.Food, "luna@demo.com");
        Assert.Null(tenant.CoverImageUrl);

        tenant.SetCoverImage("/uploads/covers/luna/portada");
        Assert.Equal("/uploads/covers/luna/portada", tenant.CoverImageUrl);

        tenant.SetCoverImage("   ");
        Assert.Null(tenant.CoverImageUrl);

        tenant.SetCoverImage("/uploads/covers/luna/otra");
        tenant.SetCoverImage(null);
        Assert.Null(tenant.CoverImageUrl);
    }

    [Fact]
    public void SetCoverImage_ShouldLeaveTheLogoAlone()
    {
        var tenant = new Tenant("Café Luna", "cafe-luna", BusinessType.Food, "luna@demo.com");
        tenant.Update(tenant.Name, tenant.ContactEmail, null, "/uploads/logos/luna/logo");

        tenant.SetCoverImage("/uploads/covers/luna/portada");

        Assert.Equal("/uploads/logos/luna/logo", tenant.LogoUrl);
    }
}
