using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>How a business shows in Explorar, the directory of TienditApp (Fase 24A, ADR 0021).</summary>
public class BusinessDirectoryTests
{
    [Fact]
    public void Categories_ShouldKeepTheirStoredValues()
    {
        // The client names each category by this number, the database keeps it and the migration's backfill writes it.
        Assert.Equal(
            new[]
            {
                "Food", "Groceries", "Health", "Apparel", "Electronics", "Hardware", "Stationery", "Beauty", "Automotive",
                "Services", "Other"
            },
            Enumerable.Range(0, 11).Select(value => ((BusinessCategory)value).ToString()));
        Assert.Equal(11, Enum.GetValues<BusinessCategory>().Length);
    }

    [Theory]
    [InlineData("Alimentos y bebidas", BusinessCategory.Food)]
    [InlineData("Comida a domicilio", BusinessCategory.Food)]
    [InlineData("Abarrotes", BusinessCategory.Groceries)]
    [InlineData("Farmacia", BusinessCategory.Health)]
    [InlineData("Consultorio", BusinessCategory.Health)]
    [InlineData("Consultorio dental", BusinessCategory.Health)]
    [InlineData("Ropa y accesorios", BusinessCategory.Apparel)]
    [InlineData("Electrónica", BusinessCategory.Electronics)]
    [InlineData("Ferretería", BusinessCategory.Hardware)]
    [InlineData("Papelería", BusinessCategory.Stationery)]
    [InlineData("Estética y spa", BusinessCategory.Beauty)]
    [InlineData("Gimnasio", BusinessCategory.Beauty)]
    [InlineData("Taller mecánico", BusinessCategory.Automotive)]
    [InlineData("Carpintería", BusinessCategory.Services)]
    [InlineData("Herrería y soldadura", BusinessCategory.Services)]
    [InlineData("Electricista", BusinessCategory.Services)]
    [InlineData("Plomería", BusinessCategory.Services)]
    [InlineData("Aire acondicionado", BusinessCategory.Services)]
    [InlineData("Servicios profesionales", BusinessCategory.Services)]
    [InlineData("Educación", BusinessCategory.Services)]
    [InlineData("Lavandería", BusinessCategory.Services)]
    public void Suggest_ShouldTakeTheCategoryOfEachGiroOfTheSignUp(string industry, BusinessCategory expected)
    {
        // Whatever the type: the giro is the closer description of what the business sells.
        foreach (var type in Enum.GetValues<BusinessType>())
            Assert.Equal(expected, BusinessCategoryDefaults.Suggest(industry, type));
    }

    [Theory]
    [InlineData("  ALIMENTOS Y BEBIDAS ")]
    [InlineData("alimentos y bebidas")]
    public void Suggest_ShouldCompareTheGiroWithoutCapitalsOrSpacesAround(string industry)
    {
        Assert.Equal(BusinessCategory.Food, BusinessCategoryDefaults.Suggest(industry, BusinessType.Retail));
        Assert.Equal(BusinessCategory.Hardware, BusinessCategoryDefaults.Suggest("Ferreteria", BusinessType.Retail));
        Assert.Equal(BusinessCategory.Services, BusinessCategoryDefaults.Suggest("LAVANDERIA", BusinessType.Retail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Otro")]
    [InlineData("Venta de flores")]
    public void Suggest_ShouldTakeTheCategoryOfTheTypeWithoutAGiroItKnows(string? industry)
    {
        Assert.Equal(BusinessCategory.Food, BusinessCategoryDefaults.Suggest(industry, BusinessType.Food));
        Assert.Equal(BusinessCategory.Services, BusinessCategoryDefaults.Suggest(industry, BusinessType.Service));
        // A shop can be of any category: the owner picks it.
        Assert.Equal(BusinessCategory.Other, BusinessCategoryDefaults.Suggest(industry, BusinessType.Retail));
    }

    [Fact]
    public void NewBusiness_ShouldStartInTheCategoryOfItsTypeWithoutDescription()
    {
        Assert.Equal(BusinessCategory.Food, new Tenant("Taquería", "taqueria", BusinessType.Food, "a@b.mx").DirectoryCategory);
        Assert.Equal(BusinessCategory.Services, new Tenant("Lupita", "lupita", BusinessType.Service, "a@b.mx").DirectoryCategory);
        var shop = new Tenant("Doña Mary", "dona-mary", BusinessType.Retail, "a@b.mx");
        Assert.Equal(BusinessCategory.Other, shop.DirectoryCategory);
        Assert.Null(shop.PublicDescription);
    }

    [Fact]
    public void Giro_ShouldGiveTheCategory_UntilTheOwnerPicksAnother()
    {
        var tenant = new Tenant("Ferretería Don Beto", "don-beto", BusinessType.Retail, "a@b.mx");

        // The sign-up and the guided setup write the giro after the business exists.
        tenant.SetBusinessInfo(null, null, "Ferretería", null, null);
        Assert.Equal(BusinessCategory.Hardware, tenant.DirectoryCategory);
        tenant.SetBusinessInfo(null, null, "Papelería", null, null);
        Assert.Equal(BusinessCategory.Stationery, tenant.DirectoryCategory);
        tenant.SetBusinessInfo(null, null, null, null, null);
        Assert.Equal(BusinessCategory.Other, tenant.DirectoryCategory);

        // Once the owner picks another, the giro no longer moves it.
        tenant.SetDirectoryProfile(BusinessCategory.Groceries, null);
        tenant.SetBusinessInfo(null, null, "Ferretería", null, null);
        Assert.Equal(BusinessCategory.Groceries, tenant.DirectoryCategory);
    }

    [Fact]
    public void Giro_ShouldNotMoveTheCategory_OnceTheOwnerDecidedIt()
    {
        var tenant = new Tenant("Ferretería Don Beto", "don-beto", BusinessType.Retail, "a@b.mx");
        tenant.SetBusinessInfo(null, null, "Ferretería", null, null);

        // The owner kept the category the giro gave: a new giro, here one written by hand, only suggests another.
        tenant.SetBusinessInfo(null, null, "Ferretería y tlapalería", null, null, keepDirectoryCategory: true);
        Assert.Equal(BusinessCategory.Hardware, tenant.DirectoryCategory);
        Assert.Equal("Ferretería y tlapalería", tenant.Industry);
    }

    [Fact]
    public void SetDirectoryProfile_ShouldKeepTheCategoryAndTheTrimmedDescription()
    {
        var tenant = new Tenant("Taquería El Güero", "el-guero", BusinessType.Food, "a@b.mx");

        tenant.SetDirectoryProfile(BusinessCategory.Food, "  Tacos al pastor desde 1998.  ");
        Assert.Equal(BusinessCategory.Food, tenant.DirectoryCategory);
        Assert.Equal("Tacos al pastor desde 1998.", tenant.PublicDescription);

        tenant.SetDirectoryProfile(BusinessCategory.Groceries, "   ");
        Assert.Equal(BusinessCategory.Groceries, tenant.DirectoryCategory);
        Assert.Null(tenant.PublicDescription);

        tenant.SetDirectoryProfile(BusinessCategory.Other, new string('a', Tenant.MaxPublicDescriptionLength));
        Assert.Equal(Tenant.MaxPublicDescriptionLength, tenant.PublicDescription!.Length);
    }

    [Theory]
    [InlineData("Tacos\ny gringas")]
    [InlineData("Tacos\ty gringas")]
    [InlineData("Tacos\u2028y gringas")]
    [InlineData("Tacos\u2029y gringas")]
    public void SetDirectoryProfile_ShouldRejectADescriptionOnMoreThanOneLine(string description)
    {
        var tenant = new Tenant("Taquería", "taqueria", BusinessType.Food, "a@b.mx");
        tenant.SetDirectoryProfile(BusinessCategory.Food, "Tacos");

        var error = Assert.Throws<DomainException>(() => tenant.SetDirectoryProfile(BusinessCategory.Groceries, description));
        Assert.Equal("La descripción va en un solo renglón.", error.Message);
        Assert.Equal(BusinessCategory.Food, tenant.DirectoryCategory);
        Assert.Equal("Tacos", tenant.PublicDescription);
    }

    [Fact]
    public void SetDirectoryProfile_ShouldRejectALongDescriptionOrACategoryThatDoesNotExist()
    {
        var tenant = new Tenant("Taquería", "taqueria", BusinessType.Food, "a@b.mx");

        Assert.Throws<DomainException>(() => tenant.SetDirectoryProfile(
            BusinessCategory.Food, new string('a', Tenant.MaxPublicDescriptionLength + 1)));
        Assert.Throws<DomainException>(() => tenant.SetDirectoryProfile((BusinessCategory)11, null));
        Assert.Equal(BusinessCategory.Food, tenant.DirectoryCategory);
        Assert.Null(tenant.PublicDescription);
    }

    [Fact]
    public void Settings_ShouldStartListedAndLetTheOwnerHideTheBusiness()
    {
        var settings = new TenantSettings(Guid.NewGuid(), TenantCapability.Food);
        Assert.True(settings.ListedInDirectory);

        settings.SetDirectoryListing(false);
        Assert.False(settings.ListedInDirectory);

        settings.SetDirectoryListing(true);
        Assert.True(settings.ListedInDirectory);
    }
}
