using System.Text.Json;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Configuración › Negocio › Plataformas de reparto (Fase 22F): the owner turns each platform on and saves the terms of
/// its contract. The list always has the three platforms, the first save is made from the defaults, every change is
/// recorded with what it was before, a save that changes nothing records nothing, and a save over an older revision is
/// refused. Whoever sells only learns which platforms are on, never their rates.
/// </summary>
public class DeliveryPlatformSettingsFlowTests
{
    [Fact]
    public async Task Platforms_ShouldBeTheThree_WithTheFiscalDefaults_UntilTheyAreSaved()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        var list = await GetAsync(fixture);

        Assert.Equal([OrderChannel.Rappi, OrderChannel.UberEats, OrderChannel.DidiFood], list.Platforms.Select(p => p.Channel));
        Assert.All(list.Platforms, platform =>
        {
            Assert.False(platform.IsConfigured);
            Assert.False(platform.IsActive);
            // Nobody's contract is made up: the commission waits for the business.
            Assert.Null(platform.CommissionPercent);
            Assert.Equal(PlatformCommissionBase.TotalWithTax, platform.CommissionBase);
            Assert.Equal((16m, 2.5m, 50m), (platform.CommissionVatPercent, platform.IsrWithholdingPercent, platform.VatWithholdingPercent));
            Assert.Equal(0, platform.Revision);
            Assert.Null(platform.ChangedAt);
            Assert.Null(platform.ChangedBy);
            Assert.Null(platform.Example);
        });
    }

    [Fact]
    public async Task FirstSave_FromRevisionZero_ShouldCreateThePlatform_AndShowWhatItDeposits()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        var result = await SaveAsync(fixture, Rappi(revision: 0));

        Assert.True(result.IsSuccess, result.Error);
        var rappi = result.Value.Platforms[0];
        Assert.Equal((OrderChannel.Rappi, true, true, 30m, 1), (rappi.Channel, rappi.IsConfigured, rappi.IsActive, rappi.CommissionPercent, rappi.Revision));
        // DiDi's published example: $1,000 with IVA included, charged by the platform.
        Assert.NotNull(rappi.Example);
        Assert.Equal((1000m, 348m, 90.52m, 561.48m),
            (rappi.Example.OrderTotal, rappi.Example.CommissionAndTax, rappi.Example.Withheld, rappi.Example.ExpectedSettlement));
        Assert.All(result.Value.Platforms.Skip(1), other => Assert.False(other.IsConfigured));

        var stored = await fixture.Context.DeliveryPlatformSettings.AsNoTracking().SingleAsync();
        Assert.Equal((tenant.Id, OrderChannel.Rappi, true, 1), (stored.TenantId, stored.Channel, stored.IsActive, stored.Revision));
        Assert.Equal(PlatformTerms.WithFiscalDefaults(30m), stored.Terms);

        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal(("DeliveryPlatformUpdated", "DeliveryPlatformSetting", stored.Id.ToString()), (audit.Action, audit.EntityName, audit.EntityId));
        Assert.Equal((tenant.Id, "cajero"), (audit.TenantId, audit.PerformedBy));
        var metadata = Metadata(audit.Metadata);
        Assert.Equal((int)OrderChannel.Rappi, metadata.GetProperty("Channel").GetInt32());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("Previous").ValueKind);
        AssertTerms(metadata.GetProperty("New"), isActive: true, commission: 30m, revision: 1);
    }

    [Fact]
    public async Task Save_WithTheRevisionSeen_ShouldChangeIt_AndRecordWhatItWas()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.True((await SaveAsync(fixture, Rappi(revision: 0))).IsSuccess);

        var result = await SaveAsync(fixture, Rappi(revision: 1) with
        {
            IsActive = false,
            CommissionPercent = 28m,
            CommissionBase = PlatformCommissionBase.SubTotalWithoutTax,
            CommissionVatPercent = 8m,
            IsrWithholdingPercent = 20m,
            VatWithholdingPercent = 100m
        });

        Assert.True(result.IsSuccess, result.Error);
        var rappi = result.Value.Platforms[0];
        Assert.Equal((false, 28m, PlatformCommissionBase.SubTotalWithoutTax, 2), (rappi.IsActive, rappi.CommissionPercent, rappi.CommissionBase, rappi.Revision));
        Assert.Equal((8m, 20m, 100m), (rappi.CommissionVatPercent, rappi.IsrWithholdingPercent, rappi.VatWithholdingPercent));
        // Without the business's RFC: 20 % of ISR and all the IVA. Commission 28 % of 862.07 = 241.38, IVA 8 % = 19.31.
        Assert.Equal((260.69m, 172.41m + 137.93m, 1000m - 260.69m - 310.34m),
            (rappi.Example!.CommissionAndTax, rappi.Example.Withheld, rappi.Example.ExpectedSettlement));

        var audits = await fixture.Context.AuditLogs.OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, audits.Count);
        var metadata = Metadata(audits.Single(a => Metadata(a.Metadata).GetProperty("Previous").ValueKind != JsonValueKind.Null).Metadata);
        AssertTerms(metadata.GetProperty("Previous"), isActive: true, commission: 30m, revision: 1);
        AssertTerms(metadata.GetProperty("New"), isActive: false, commission: 28m, revision: 2);
        Assert.Equal((int)PlatformCommissionBase.SubTotalWithoutTax, metadata.GetProperty("New").GetProperty("CommissionBase").GetInt32());
    }

    [Fact]
    public async Task SaveThatChangesNothing_ShouldSaveAndRecordNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.True((await SaveAsync(fixture, Rappi(revision: 0))).IsSuccess);

        var again = await SaveAsync(fixture, Rappi(revision: 1));

        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(1, again.Value.Platforms[0].Revision);
        Assert.Equal(1, (await fixture.Context.DeliveryPlatformSettings.AsNoTracking().SingleAsync()).Revision);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task SaveOverAnOlderRevision_ShouldBeStale_AndChangeNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.True((await SaveAsync(fixture, Rappi(revision: 0))).IsSuccess);
        Assert.True((await SaveAsync(fixture, Rappi(revision: 1) with { CommissionPercent = 28m })).IsSuccess);

        // Someone else saved Rappi since this person loaded it; and a first save of Uber Eats from a revision it never had.
        var stale = await SaveAsync(fixture, Rappi(revision: 1) with { CommissionPercent = 25m });
        var neverSaved = await SaveAsync(fixture, Rappi(revision: 3) with { Channel = OrderChannel.UberEats });
        var firstAgain = await SaveAsync(fixture, Rappi(revision: 0) with { CommissionPercent = 25m });

        Assert.Equal(DeliveryPlatformErrors.Stale, stale.Error);
        Assert.Equal(DeliveryPlatformErrors.Stale, neverSaved.Error);
        Assert.Equal(DeliveryPlatformErrors.Stale, firstAgain.Error);
        // The same words as any other change saved at the same time.
        Assert.Equal(SpecialDayErrors.Stale, DeliveryPlatformErrors.Stale);
        var stored = await fixture.Context.DeliveryPlatformSettings.AsNoTracking().SingleAsync();
        Assert.Equal((OrderChannel.Rappi, 28m, 2), (stored.Channel, stored.CommissionPercent, stored.Revision));
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(8)]
    [InlineData(0)]
    public void CommissionVat_ShouldBeARateABusinessCanHave(int rate)
    {
        var validator = new SaveDeliveryPlatformCommandValidator();

        Assert.True(validator.Validate(Rappi(revision: 0) with { CommissionVatPercent = rate }).IsValid);
    }

    [Fact]
    public void Validator_ShouldTakeOnlyThePlatforms_AndRatesWithinTheirLimits()
    {
        var validator = new SaveDeliveryPlatformCommandValidator();

        Assert.True(validator.Validate(Rappi(revision: 0)).IsValid);
        // Rappi gives 0 % the first 30 days; the highest limits are allowed.
        Assert.True(validator.Validate(Rappi(revision: 0) with { CommissionPercent = 0m, IsrWithholdingPercent = 20m, VatWithholdingPercent = 100m }).IsValid);
        Assert.True(validator.Validate(Rappi(revision: 4) with { CommissionPercent = 60m, IsrWithholdingPercent = 0m, VatWithholdingPercent = 0m }).IsValid);
        AssertRefused(validator, Rappi(0) with { Channel = OrderChannel.Counter }, "Esa no es una plataforma de reparto.");
        AssertRefused(validator, Rappi(0) with { Channel = OrderChannel.WhatsApp }, "Esa no es una plataforma de reparto.");
        AssertRefused(validator, Rappi(0) with { Channel = (OrderChannel)9 }, "Esa no es una plataforma de reparto.");
        AssertRefused(validator, Rappi(-1), "Recarga e intenta de nuevo.");
        AssertRefused(validator, Rappi(0) with { CommissionPercent = 60.01m }, "La comisión va de 0 % a 60 %, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { CommissionPercent = -1m }, "La comisión va de 0 % a 60 %, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { CommissionPercent = 12.345m }, "La comisión va de 0 % a 60 %, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { CommissionVatPercent = 10m }, VatRateRules.InvalidRateMessage);
        AssertRefused(validator, Rappi(0) with { IsrWithholdingPercent = 20.01m }, "La retención de ISR va de 0 % a 20 %, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { IsrWithholdingPercent = 2.505m }, "La retención de ISR va de 0 % a 20 %, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { VatWithholdingPercent = 100.5m },
            "La retención de IVA va de 0 % a 100 % del IVA del pedido, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { VatWithholdingPercent = -0.01m },
            "La retención de IVA va de 0 % a 100 % del IVA del pedido, con hasta dos decimales.");
        AssertRefused(validator, Rappi(0) with { CommissionBase = (PlatformCommissionBase)7 }, "Elige sobre qué se cobra la comisión.");
    }

    [Fact]
    public async Task TermsOutsideTheDomainLimits_ShouldBeRefusedByTheDomainToo()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);

        // Past the request validation (another caller of the handler): the domain repeats the limits.
        var result = await SaveAsync(fixture, Rappi(revision: 0) with { CommissionPercent = 75m });

        Assert.Equal(PlatformTerms.CommissionMessage, result.Error);
        Assert.Empty(await fixture.Context.DeliveryPlatformSettings.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task StoredTermsOutsideTodaysLimits_ShouldStillShow_WithoutAnExample()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var setting = await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood);
        // Written before a limit went down: the owner must still see it to fix it.
        fixture.Context.Entry(setting).Property(s => s.CommissionPercent).CurrentValue = 70m;
        await fixture.Context.SaveChangesAsync();

        var didi = (await GetAsync(fixture)).Platforms.Single(p => p.Channel == OrderChannel.DidiFood);

        Assert.Equal((true, 70m), (didi.IsConfigured, didi.CommissionPercent));
        Assert.Null(didi.Example);
    }

    [Fact]
    public async Task EachBusiness_ShouldSeeAndChangeOnlyItsOwnPlatforms()
    {
        await using var fixture = SalesTestFixture.Create();
        var mine = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "mia");
        var theirs = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otra");
        fixture.TenantService.SetTenant(mine.Id);
        Assert.True((await SaveAsync(fixture, Rappi(revision: 0))).IsSuccess);

        // The other business sees Rappi as never saved, and its first save makes its own row.
        fixture.TenantService.SetTenant(theirs.Id);
        var theirList = await GetAsync(fixture);
        var theirSave = await SaveAsync(fixture, Rappi(revision: 0) with { CommissionPercent = 18m, IsActive = false });
        var theirChannels = await ChannelsAsync(fixture);

        Assert.False(theirList.Platforms[0].IsConfigured);
        Assert.True(theirSave.IsSuccess, theirSave.Error);
        Assert.Equal((18m, false, 1), (theirSave.Value.Platforms[0].CommissionPercent, theirSave.Value.Platforms[0].IsActive, theirSave.Value.Platforms[0].Revision));
        Assert.Equal([(OrderChannel.Rappi, false)], theirChannels.Select(c => (c.Channel, c.IsActive)));

        fixture.TenantService.SetTenant(mine.Id);
        var myRappi = (await GetAsync(fixture)).Platforms[0];
        Assert.Equal((30m, true, 1), (myRappi.CommissionPercent, myRappi.IsActive, myRappi.Revision));
        Assert.Equal(mine.Id, (await fixture.Context.AuditLogs.SingleAsync()).TenantId);
    }

    [Fact]
    public async Task Channels_ShouldListTheSavedPlatforms_WithoutTheirRates()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.Empty(await ChannelsAsync(fixture));
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.DidiFood, isActive: false);
        await fixture.AddDeliveryPlatformAsync(tenant, OrderChannel.Rappi);

        var channels = await ChannelsAsync(fixture);

        Assert.Equal([(OrderChannel.Rappi, true), (OrderChannel.DidiFood, false)], channels.Select(c => (c.Channel, c.IsActive)));
        // Only which platforms exist and whether they take orders: no commission, withholding or revision.
        Assert.Equal(
            [nameof(DeliveryPlatformChannelDto.Channel), nameof(DeliveryPlatformChannelDto.IsActive)],
            typeof(DeliveryPlatformChannelDto).GetProperties().Select(p => p.Name).Order());
    }

    private static SaveDeliveryPlatformCommand Rappi(int revision) => new(
        OrderChannel.Rappi,
        revision,
        IsActive: true,
        CommissionPercent: 30m,
        CommissionBase: PlatformCommissionBase.TotalWithTax,
        CommissionVatPercent: 16m,
        IsrWithholdingPercent: 2.5m,
        VatWithholdingPercent: 50m);

    /// <summary>Each request with a context of its own, as in the app.</summary>
    private static async Task<Result<DeliveryPlatformsDto>> SaveAsync(SalesTestFixture fixture, SaveDeliveryPlatformCommand command)
    {
        var result = await new SaveDeliveryPlatformCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(command, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<DeliveryPlatformsDto> GetAsync(SalesTestFixture fixture)
        => new GetDeliveryPlatformsQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetDeliveryPlatformsQuery(), CancellationToken.None);

    private static Task<List<DeliveryPlatformChannelDto>> ChannelsAsync(SalesTestFixture fixture)
        => new GetDeliveryPlatformChannelsQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetDeliveryPlatformChannelsQuery(), CancellationToken.None);

    private static void AssertRefused(SaveDeliveryPlatformCommandValidator validator, SaveDeliveryPlatformCommand command, string message)
    {
        var result = validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Equal([message], result.Errors.Select(e => e.ErrorMessage));
    }

    private static void AssertTerms(JsonElement terms, bool isActive, decimal commission, int revision)
    {
        Assert.Equal(isActive, terms.GetProperty("IsActive").GetBoolean());
        Assert.Equal(commission, terms.GetProperty("CommissionPercent").GetDecimal());
        Assert.Equal(revision, terms.GetProperty("Revision").GetInt32());
        Assert.True(terms.TryGetProperty("CommissionVatPercent", out _));
        Assert.True(terms.TryGetProperty("IsrWithholdingPercent", out _));
        Assert.True(terms.TryGetProperty("VatWithholdingPercent", out _));
    }

    private static JsonElement Metadata(string? metadata) => JsonDocument.Parse(metadata!).RootElement;
}
