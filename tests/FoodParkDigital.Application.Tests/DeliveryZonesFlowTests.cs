using System.Text.Json;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The delivery zones of a business (Fase 22B): added, saved with the revision seen and deleted by owners and admins, each
/// change recorded with what it charges, a postal code in one zone of the business at most (naming the zone that has it),
/// up to 30 zones, and every business with its own.
/// </summary>
public class DeliveryZonesFlowTests
{
    [Fact]
    public async Task Create_ShouldSaveTheZone_WithItsPostalCodesOnceEach_AndRecordWhatItCharges()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);

        var result = await CreateAsync(fixture, " Centro ", fee: 35m, minimum: 150m, freeFrom: 400m, " 72005", "72000", "72005");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Centro", result.Value.Name);
        Assert.Equal((35m, 150m, 400m, true, 1), (result.Value.Fee, result.Value.MinimumOrder, result.Value.FreeDeliveryFrom, result.Value.IsActive, result.Value.Revision));
        Assert.Equal(["72000", "72005"], result.Value.PostalCodes);
        var entry = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal(("DeliveryZoneCreated", nameof(DeliveryZone), result.Value.Id.ToString(), tenant.Id), (entry.Action, entry.EntityName, entry.EntityId, entry.TenantId));
        var recorded = JsonDocument.Parse(entry.Metadata!).RootElement.GetProperty("New");
        Assert.Equal(35m, recorded.GetProperty("Fee").GetDecimal());
        Assert.Equal(2, recorded.GetProperty("PostalCodes").GetInt32());
        // How many postal codes, never which: a zone can have hundreds.
        Assert.DoesNotContain("72000", entry.Metadata);
    }

    [Fact]
    public async Task Create_ShouldRefuse_APostalCodeOfAnotherZone_NamingIt_WhileAnotherBusinessCanHaveIt()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000", "72005");

        var result = await CreateAsync(fixture, "Angelópolis", 45m, 0m, null, "72830", "72005");

        Assert.True(result.IsFailure);
        Assert.Equal(DeliveryZoneErrors.PostalCodeInAnotherZone("72005", "Centro"), result.Error);
        Assert.Single(await fixture.Context.DeliveryZones.ToListAsync());

        await BusinessAsync(fixture, "fonda-vecina");
        Assert.True((await CreateAsync(fixture, "Centro", 30m, 0m, null, "72005")).IsSuccess);
    }

    [Fact]
    public async Task CreateAndUpdate_ShouldRefuse_TheNameOfAnotherZone_WhileAnotherBusinessCanHaveIt()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000");
        var norte = (await CreateAsync(fixture, "Norte", 45m, 0m, null, "72100")).Value;

        var created = await CreateAsync(fixture, " Centro ", 40m, 0m, null, "72005");
        var renamed = await UpdateAsync(fixture, norte.Id, norte.Revision, "Centro", 45m, 0m, null, true, "72100");

        Assert.Equal(DeliveryZoneErrors.NameTaken("Centro"), created.Error);
        Assert.Equal(DeliveryZoneErrors.NameTaken("Centro"), renamed.Error);
        // The refused rename is undone: the zone keeps its name and revision for the next save.
        var saved = await UpdateAsync(fixture, norte.Id, norte.Revision, "Norte", 50m, 0m, null, true, "72100");
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(("Norte", 2), (saved.Value.Name, saved.Value.Revision));
        Assert.Equal(2, await fixture.Context.DeliveryZones.CountAsync());

        await BusinessAsync(fixture, "fonda-vecina");
        Assert.True((await CreateAsync(fixture, "Centro", 30m, 0m, null, "72000")).IsSuccess);
    }

    [Fact]
    public async Task Create_ShouldStopAt30Zones()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        for (var zone = 0; zone < DeliveryZone.MaxZonesPerBusiness; zone++)
            Assert.True((await CreateAsync(fixture, $"Zona {zone}", 35m, 0m, null, (72000 + zone).ToString())).IsSuccess);

        var result = await CreateAsync(fixture, "Una más", 35m, 0m, null, "73000");

        Assert.Equal(DeliveryZone.TooManyZonesMessage, result.Error);
    }

    [Theory]
    [InlineData(35.005, "72000", DeliveryZone.FeeMessage)]
    [InlineData(0, "72000", DeliveryZone.FreeDeliveryWithoutFeeMessage)]
    [InlineData(35, "7200", "«7200» no es un código postal: lleva 5 dígitos.")]
    public async Task Create_ShouldAnswerWhatIsWrong_AndSaveNothing(double fee, string postalCode, string message)
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);

        var result = await CreateAsync(fixture, "Centro", (decimal)fee, 0m, 400m, postalCode);

        Assert.Equal(message, result.Error);
        Assert.Empty(await fixture.Context.DeliveryZones.ToListAsync());
        Assert.Empty(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Update_ShouldSaveWithTheRevisionSeen_RecordBeforeAndAfter_AndRefuseAnOlderRevision()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        var zone = (await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000")).Value;

        var saved = await UpdateAsync(fixture, zone.Id, zone.Revision, "Centro", 40m, 100m, null, false, "72000", "72010");

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((40m, 100m, false, 2), (saved.Value.Fee, saved.Value.MinimumOrder, saved.Value.IsActive, saved.Value.Revision));
        Assert.Equal(["72000", "72010"], saved.Value.PostalCodes);
        var entry = await fixture.Context.AuditLogs.SingleAsync(log => log.Action == "DeliveryZoneUpdated");
        var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
        Assert.Equal(35m, metadata.GetProperty("Previous").GetProperty("Fee").GetDecimal());
        Assert.Equal(40m, metadata.GetProperty("New").GetProperty("Fee").GetDecimal());

        // A tab that still shows revision 1 cannot overwrite the new fee.
        var stale = await UpdateAsync(fixture, zone.Id, zone.Revision, "Centro", 30m, 0m, null, true, "72000");
        Assert.Equal(DeliveryZoneErrors.Stale, stale.Error);
        Assert.Equal(40m, (await fixture.Context.DeliveryZones.AsNoTracking().SingleAsync()).Fee);
    }

    [Fact]
    public async Task Update_WithNothingChanged_ShouldKeepTheRevision_AndRecordNothing()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        var zone = (await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000", "72005")).Value;

        var saved = await UpdateAsync(fixture, zone.Id, zone.Revision, "Centro ", 35m, 0m, null, true, "72005", "72000");

        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal(1, saved.Value.Revision);
        Assert.Single(await fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Update_ShouldRefuse_TakingAPostalCodeOfAnotherZone_UntilThatZoneGivesItUp()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        var centro = (await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000", "72005")).Value;
        var norte = (await CreateAsync(fixture, "Norte", 45m, 0m, null, "72100")).Value;

        var refused = await UpdateAsync(fixture, norte.Id, norte.Revision, "Norte", 45m, 0m, null, true, "72100", "72005");
        Assert.Equal(DeliveryZoneErrors.PostalCodeInAnotherZone("72005", "Centro"), refused.Error);

        Assert.True((await UpdateAsync(fixture, centro.Id, centro.Revision, "Centro", 35m, 0m, null, true, "72000")).IsSuccess);
        var moved = await UpdateAsync(fixture, norte.Id, norte.Revision, "Norte", 45m, 0m, null, true, "72100", "72005");
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(["72005", "72100"], moved.Value.PostalCodes);
    }

    [Fact]
    public async Task Delete_ShouldFreeItsPostalCodes_RecordIt_AndRefuseAnOlderRevision()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        var zone = (await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000")).Value;
        await UpdateAsync(fixture, zone.Id, zone.Revision, "Centro", 40m, 0m, null, true, "72000");

        var stale = await DeleteAsync(fixture, zone.Id, zone.Revision);
        Assert.Equal(DeliveryZoneErrors.Stale, stale.Error);

        var deleted = await DeleteAsync(fixture, zone.Id, zone.Revision + 1);
        Assert.True(deleted.IsSuccess, deleted.Error);
        Assert.Empty(await fixture.Context.DeliveryZones.ToListAsync());
        Assert.Single(await fixture.Context.AuditLogs.Where(log => log.Action == "DeliveryZoneDeleted").ToListAsync());
        Assert.True((await CreateAsync(fixture, "Otra", 30m, 0m, null, "72000")).IsSuccess);
    }

    [Fact]
    public async Task ZoneOfAnotherBusiness_ShouldNotBeFound_ToReadSaveOrDelete()
    {
        await using var fixture = SalesTestFixture.Create();
        await BusinessAsync(fixture);
        var theirs = (await CreateAsync(fixture, "Centro", 35m, 0m, null, "72000")).Value;
        await BusinessAsync(fixture, "fonda-vecina");

        Assert.Empty(await new GetDeliveryZonesQueryHandler(fixture.Context).Handle(new GetDeliveryZonesQuery(), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => UpdateAsync(fixture, theirs.Id, theirs.Revision, "Mía", 10m, 0m, null, true, "72000"));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => DeleteAsync(fixture, theirs.Id, theirs.Revision));
    }

    [Fact]
    public async Task Zones_ShouldBeListedByName_AndTheLookupShouldFindOnlyAnActiveZone()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await BusinessAsync(fixture);
        await CreateAsync(fixture, "Norte", 45m, 0m, null, "72100");
        var centro = (await CreateAsync(fixture, "Centro", 35m, 150m, 400m, "72000")).Value;

        var zones = await new GetDeliveryZonesQueryHandler(fixture.Context).Handle(new GetDeliveryZonesQuery(), CancellationToken.None);
        Assert.Equal(["Centro", "Norte"], zones.Select(zone => zone.Name));

        Assert.Equal(new DeliveryZoneCoverage("Centro", 35m, 150m, 400m),
            await DeliveryZoneLookup.FindAsync(fixture.Context, tenant.Id, " 72000 ", CancellationToken.None));
        Assert.Null(await DeliveryZoneLookup.FindAsync(fixture.Context, tenant.Id, "72001", CancellationToken.None));
        Assert.Null(await DeliveryZoneLookup.FindAsync(fixture.Context, tenant.Id, "7200", CancellationToken.None));
        Assert.Null(await DeliveryZoneLookup.FindAsync(fixture.Context, tenant.Id, null, CancellationToken.None));

        await UpdateAsync(fixture, centro.Id, centro.Revision, "Centro", 35m, 150m, 400m, false, "72000");
        Assert.Null(await DeliveryZoneLookup.FindAsync(fixture.Context, tenant.Id, "72000", CancellationToken.None));
        Assert.True(await DeliveryZoneLookup.AnyActiveAsync(fixture.Context, tenant.Id, CancellationToken.None));
    }

    [Fact]
    public void Validators_ShouldRefuse_AMissingName_TooLongAName_AndAListOfPostalCodesNoZoneHolds()
    {
        var validator = new CreateDeliveryZoneCommandValidator();

        Assert.Equal(DeliveryZone.NameRequiredMessage, FirstError(validator.Validate(Command(" "))));
        Assert.Equal(DeliveryZone.NameTooLongMessage, FirstError(validator.Validate(Command(new string('a', 61)))));
        Assert.Equal(DeliveryZone.PostalCodesRequiredMessage, FirstError(validator.Validate(Command("Centro") with { PostalCodes = null! })));
        var tooMany = Enumerable.Repeat("72000", DeliveryZoneErrors.MaxWritten + 1).ToList();
        Assert.Equal(DeliveryZone.TooManyPostalCodesMessage, FirstError(validator.Validate(Command("Centro") with { PostalCodes = tooMany })));
        Assert.True(validator.Validate(Command(" Centro ")).IsValid);

        var update = new UpdateDeliveryZoneCommandValidator();
        Assert.False(update.Validate(new UpdateDeliveryZoneCommand(Guid.NewGuid(), 0, "Centro", 35m, 0m, null, ["72000"], true)).IsValid);
        Assert.False(new DeleteDeliveryZoneCommandValidator().Validate(new DeleteDeliveryZoneCommand(Guid.Empty, 1)).IsValid);

        static CreateDeliveryZoneCommand Command(string name) => new(name, 35m, 0m, null, ["72000"]);
        static string? FirstError(FluentValidation.Results.ValidationResult result) => result.Errors.FirstOrDefault()?.ErrorMessage;
    }

    private static async Task<Tenant> BusinessAsync(SalesTestFixture fixture, string slug = "demo")
        => await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);

    private static Task<Common.Models.Result<DeliveryZoneDto>> CreateAsync(
        SalesTestFixture fixture, string name, decimal fee, decimal minimum, decimal? freeFrom, params string[] postalCodes)
        => new CreateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new CreateDeliveryZoneCommand(name, fee, minimum, freeFrom, postalCodes), CancellationToken.None);

    private static Task<Common.Models.Result<DeliveryZoneDto>> UpdateAsync(
        SalesTestFixture fixture, Guid id, int revision, string name, decimal fee, decimal minimum, decimal? freeFrom, bool isActive,
        params string[] postalCodes)
        => new UpdateDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new UpdateDeliveryZoneCommand(id, revision, name, fee, minimum, freeFrom, postalCodes, isActive), CancellationToken.None);

    private static Task<Common.Models.Result> DeleteAsync(SalesTestFixture fixture, Guid id, int revision)
        => new DeleteDeliveryZoneCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new DeleteDeliveryZoneCommand(id, revision), CancellationToken.None);
}
