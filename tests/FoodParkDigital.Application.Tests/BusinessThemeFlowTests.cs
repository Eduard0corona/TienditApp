using System.Text.Json;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>The theme the owner picks: stored per business, recorded, and returned where the client paints it.</summary>
public class BusinessThemeFlowTests
{
    [Fact]
    public async Task UpdateTheme_ShouldChangeTheBusinessAndRecordWhoChangedItAndFromWhat()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "cafe-luna");

        var result = await Handler(fixture).Handle(new UpdateBusinessThemeCommand(BusinessTheme.RosaMexicano), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(BusinessTheme.RosaMexicano, result.Value.Theme);
        var settings = await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id);
        Assert.Equal(BusinessTheme.RosaMexicano, settings.Theme);
        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal("BusinessThemeUpdated", audit.Action);
        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Equal("cajero", audit.PerformedBy);
        var metadata = JsonDocument.Parse(audit.Metadata!).RootElement;
        Assert.Equal("Nopal", metadata.GetProperty("Previous").GetString());
        Assert.Equal("RosaMexicano", metadata.GetProperty("New").GetString());
    }

    [Fact]
    public async Task UpdateTheme_ToTheSameTheme_ShouldNotRecordItAgain()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);

        await Handler(fixture).Handle(new UpdateBusinessThemeCommand(BusinessTheme.Talavera), CancellationToken.None);
        var again = await Handler(fixture).Handle(new UpdateBusinessThemeCommand(BusinessTheme.Talavera), CancellationToken.None);

        Assert.True(again.IsSuccess, again.Error);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UpdateTheme_ShouldTouchOnlyTheBusinessInScope()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var mine = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "mio");

        await Handler(fixture).Handle(new UpdateBusinessThemeCommand(BusinessTheme.Grafito), CancellationToken.None);

        var themes = await fixture.Context.TenantSettings.IgnoreQueryFilters()
            .ToDictionaryAsync(s => s.TenantId, s => s.Theme);
        Assert.Equal(BusinessTheme.Grafito, themes[mine.Id]);
        Assert.Equal(BusinessTheme.Nopal, themes[other.Id]);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, false)]
    public void Validator_ShouldOnlyAcceptThemesThatExist(int value, bool valid)
    {
        var result = new UpdateBusinessThemeCommandValidator().Validate(new UpdateBusinessThemeCommand((BusinessTheme)value));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task Profile_ShouldCarryTheThemeTheClientPaints()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        Assert.Equal(BusinessTheme.Nopal, (await LoadAsync(fixture, tenant.Id)).ToProfileDto().Theme);

        await Handler(fixture).Handle(new UpdateBusinessThemeCommand(BusinessTheme.CafeDeOlla), CancellationToken.None);

        Assert.Equal(BusinessTheme.CafeDeOlla, (await LoadAsync(fixture, tenant.Id)).ToProfileDto().Theme);
    }

    private static Task<Domain.Tenants.Entities.Tenant> LoadAsync(SalesTestFixture fixture, Guid tenantId)
        => fixture.Context.Tenants.AsNoTracking().Include(t => t.Settings).SingleAsync(t => t.Id == tenantId);

    private static UpdateBusinessThemeCommandHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, fixture.TenantService);
}
