using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>The visits to quote of the trades (Fase 29A, ADR 0053): the settings of a business and the trade giros.</summary>
public class HomeVisitRequestsTests
{
    [Fact]
    public void Settings_ShouldStartWithoutVisits_Charged_AndTheNoticeShowing()
    {
        var settings = new TenantSettings(Guid.NewGuid());

        Assert.Equal((false, (Guid?)null, HomeVisitChargePolicy.Charged, (DateTimeOffset?)null),
            (settings.HomeVisitRequestsEnabled, settings.HomeVisitServiceId, settings.HomeVisitChargePolicy,
                settings.HomeVisitAddressNoticeDismissedAt));
        // The database keeps the number.
        Assert.Equal(new[] { 0, 1, 2 }, Enum.GetValues<HomeVisitChargePolicy>().Select(p => (int)p));
    }

    [Fact]
    public void Update_ShouldKeepTheVisitsItsServiceAndHowItIsCharged()
    {
        var settings = new TenantSettings(Guid.NewGuid());
        var service = Guid.NewGuid();

        settings.UpdateHomeVisitRequests(true, service, HomeVisitChargePolicy.CreditedIfHired);
        Assert.Equal((true, (Guid?)service, HomeVisitChargePolicy.CreditedIfHired),
            (settings.HomeVisitRequestsEnabled, settings.HomeVisitServiceId, settings.HomeVisitChargePolicy));

        // Without a service the visit costs its zone; off keeps what was chosen for when it is on again.
        settings.UpdateHomeVisitRequests(false, null, HomeVisitChargePolicy.Free);
        Assert.Equal((false, (Guid?)null, HomeVisitChargePolicy.Free),
            (settings.HomeVisitRequestsEnabled, settings.HomeVisitServiceId, settings.HomeVisitChargePolicy));
    }

    [Fact]
    public void Update_ShouldRefuseAPolicyThatDoesNotExist_OrAnEmptyService()
    {
        var settings = new TenantSettings(Guid.NewGuid());

        Assert.Equal(HomeVisitRules.ChargePolicyMessage, Assert.Throws<DomainException>(() =>
            settings.UpdateHomeVisitRequests(true, null, (HomeVisitChargePolicy)3)).Message);
        Assert.Equal(HomeVisitRules.VisitServiceMessage, Assert.Throws<DomainException>(() =>
            settings.UpdateHomeVisitRequests(true, Guid.Empty, HomeVisitChargePolicy.Charged)).Message);
        Assert.False(settings.HomeVisitRequestsEnabled);
    }

    [Fact]
    public void TemplateService_ShouldBeTheVisitOnlyWhileThereIsNone()
    {
        var settings = new TenantSettings(Guid.NewGuid());
        var first = Guid.NewGuid();

        settings.SuggestHomeVisitService(first);
        settings.SuggestHomeVisitService(Guid.NewGuid());

        Assert.Equal(first, settings.HomeVisitServiceId);
        Assert.Throws<DomainException>(() => new TenantSettings(Guid.NewGuid()).SuggestHomeVisitService(Guid.Empty));
    }

    [Fact]
    public void Notice_ShouldBeDismissedOnce_KeepingTheFirstTime()
    {
        var settings = new TenantSettings(Guid.NewGuid());
        var first = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

        settings.DismissHomeVisitAddressNotice(first);
        settings.DismissHomeVisitAddressNotice(first.AddDays(1));

        Assert.Equal(first, settings.HomeVisitAddressNoticeDismissedAt);
    }

    [Fact]
    public void SigningUp_ShouldStartReceivingVisits()
    {
        var settings = new TenantSettings(Guid.NewGuid());

        settings.StartReceivingHomeVisits();

        Assert.True(settings.HomeVisitRequestsEnabled);
    }

    [Theory]
    [InlineData("Carpintería", true, false)]
    [InlineData("Herrería y soldadura", true, false)]
    [InlineData("Electricista", true, true)]
    [InlineData("Plomería", true, true)]
    [InlineData("Aire acondicionado", true, true)]
    [InlineData("Fumigación", true, true)]
    [InlineData("Refrigeración y línea blanca", true, true)]
    [InlineData("Cerrajería", true, true)]
    [InlineData("Jardinería", true, true)]
    [InlineData("Limpieza del hogar", true, true)]
    [InlineData("Pintura e impermeabilización", true, true)]
    [InlineData("  PLOMERIA ", true, true)]
    [InlineData("Comida a domicilio", false, false)]
    [InlineData("Servicios profesionales", false, false)]
    [InlineData("Ferretería", false, false)]
    [InlineData(null, false, false)]
    public void Trades_ShouldBeTheGirosThatGoHome_AndOnlyTheOnesWithoutAWorkshopStartWithoutPremises(
        string? industry, bool isTrade, bool withoutPremises)
    {
        Assert.Equal(isTrade, HomeTrades.IsHomeTrade(industry));
        Assert.Equal(withoutPremises, HomeTrades.StartsWithoutPremises(industry));
        if (isTrade)
            Assert.Equal(BusinessCategory.HomeRepairs, BusinessCategoryDefaults.Suggest(industry, BusinessType.Service));
    }

    [Fact]
    public void Trades_ShouldBeElevenGiros_InTheOrderOfTheSignUp()
    {
        Assert.Equal(11, HomeTrades.All.Count);
        Assert.Equal(HomeTrades.All.Count, HomeTrades.All.Distinct().Count());
        Assert.Equal(new[] { HomeTrades.Carpentry, HomeTrades.Welding }, HomeTrades.WithWorkshop);
    }

    [Fact]
    public void ATrade_ShouldMoveToHomeRepairsWithItsGiro_WhileItsCategoryFollowsIt()
    {
        var tenant = new Tenant("Plomería Ramírez", "plomeria-ramirez", BusinessType.Service, "ramirez@demo.com");

        tenant.SetBusinessInfo(null, null, "Plomería", null, null);
        Assert.Equal(BusinessCategory.HomeRepairs, tenant.DirectoryCategory);

        // Its owner decided the category: the giro no longer moves it.
        tenant.SetDirectoryProfile(BusinessCategory.Hardware, null);
        tenant.SetBusinessInfo(null, null, "Electricista", null, null, keepDirectoryCategory: true);
        Assert.Equal(BusinessCategory.Hardware, tenant.DirectoryCategory);
    }
}
