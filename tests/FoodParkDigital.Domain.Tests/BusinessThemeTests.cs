using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The color theme the owner picks for the business (Configuración > Negocio > Apariencia).</summary>
public class BusinessThemeTests
{
    [Fact]
    public void Settings_ShouldStartWithTienditAppsTheme()
    {
        Assert.Equal(BusinessTheme.Nopal, new TenantSettings(Guid.NewGuid(), TenantCapability.Food).Theme);
        Assert.Equal(0, (int)BusinessTheme.Nopal);
    }

    [Fact]
    public void SetTheme_ShouldTakeEachThemeAndRejectOneThatDoesNotExist()
    {
        var settings = new TenantSettings(Guid.NewGuid(), TenantCapability.Retail);
        foreach (var theme in Enum.GetValues<BusinessTheme>())
        {
            settings.SetTheme(theme);
            Assert.Equal(theme, settings.Theme);
        }

        settings.SetTheme(BusinessTheme.Talavera);
        Assert.Throws<DomainException>(() => settings.SetTheme((BusinessTheme)99));
        Assert.Equal(BusinessTheme.Talavera, settings.Theme);
    }

    [Fact]
    public void Themes_ShouldKeepTheirStoredValues()
    {
        // The client paints each theme by this number and the database keeps it: reordering would repaint businesses.
        Assert.Equal(
            new[] { "Nopal", "Talavera", "Jacaranda", "RosaMexicano", "Jamaica", "Cempasuchil", "CafeDeOlla", "Grafito" },
            Enumerable.Range(0, 8).Select(value => ((BusinessTheme)value).ToString()));
        Assert.Equal(8, Enum.GetValues<BusinessTheme>().Length);
    }
}
