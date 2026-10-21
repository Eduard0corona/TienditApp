using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Conteo físico (Fase 27A, ADR 0037): a location's count records what is on the shelf against what the system had when
/// each item was counted; posting it moves the stock by the differences, and once posted or cancelled it no longer changes.
/// </summary>
public class InventoryCountTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void New_ShouldBeOpen_WithItsFolioLocationAndWhoStartedIt()
    {
        var count = new InventoryCount(
            Tenant, "CNT-20261021-0001", Location, Now, "  dueno@tienda.mx ", "  Bebidas  ", "   ");

        Assert.Equal(
            (Tenant, "CNT-20261021-0001", Location, InventoryCountStatus.InProgress, "Bebidas", (string?)null),
            (count.TenantId, count.CountNumber, count.InventoryLocationId, count.Status, count.CategoryName, count.Notes));
        Assert.Equal((Now, "dueno@tienda.mx", 0, 0, 0), (count.StartedAt, count.StartedBy, count.LineCount, count.CountedLineCount, count.Revision));
        Assert.True(count.IsOpen);
        Assert.Null(count.PostedAt);
        Assert.Null(count.CancelledAt);
    }

    [Fact]
    public void New_ShouldNeedALocation()
    {
        var error = Assert.Throws<DomainException>(() => new InventoryCount(Tenant, "CNT-20261021-0001", Guid.Empty, Now, "dueno"));

        Assert.Equal("Elige la ubicación que vas a contar.", error.Message);
    }

    [Fact]
    public void Folio_ShouldStartAgainEachUtcDay()
    {
        var scope = Folios.InventoryCountScope(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(-6)));

        Assert.Equal("CNT-20261022", scope);
        Assert.Equal("CNT-20261022-0007", Folios.DailyNumber(scope, 7));
    }

    [Fact]
    public void AddLine_ShouldStartUncounted_AndRaiseTheRevision()
    {
        var count = NewCount();
        var supply = Guid.NewGuid();

        var line = count.AddLine(supply);

        Assert.Equal((Tenant, count.Id, supply), (line.TenantId, line.CountId, line.SupplyItemId));
        Assert.False(line.IsCounted);
        Assert.Null(line.Difference);
        Assert.Null(line.ResultingQuantity(5m));
        Assert.Equal((1, 0, 1), (count.LineCount, count.CountedLineCount, count.Revision));
    }

    [Fact]
    public void AddLine_ShouldStopAtTheMostItemsOfACount()
    {
        var count = NewCount();
        for (var i = 0; i < InventoryCount.MaxLines; i++)
            count.AddLine(Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => count.AddLine(Guid.NewGuid()));

        Assert.Equal(InventoryCount.TooManyLinesMessage, error.Message);
        Assert.Equal(InventoryCount.MaxLines, count.LineCount);
    }

    [Fact]
    public void Record_ShouldKeepWhatTheSystemHadWhenItWasCounted()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());

        count.Record(line, 8m, 10m, UnitType.Unit, Now, "  cajero ", "  Una lata abollada  ");

        Assert.Equal(((decimal?)8m, (decimal?)10m, (DateTimeOffset?)Now, "cajero", "Una lata abollada"),
            (line.CountedQuantity, line.SystemQuantity, line.CountedAt, line.CountedBy, line.Notes));
        Assert.Equal(-2m, line.Difference);
        Assert.Equal((1, 2), (count.CountedLineCount, count.Revision));
    }

    [Fact]
    public void ResultingQuantity_ShouldKeepWhatMovedAfterTheItemWasCounted()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());
        count.Record(line, 8m, 10m, UnitType.Unit, Now, "cajero");

        // Three sold after counting: the shelf has 5, not the 8 that were counted.
        Assert.Equal(5m, line.ResultingQuantity(7m));
    }

    [Fact]
    public void Record_ShouldKeepWhatTheSystemHad_WhenTheQuantityIsCorrected()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());
        // 10 typed instead of 12; two are sold before it is corrected.
        count.Record(line, 10m, 12m, UnitType.Unit, Now, "cajero");

        count.Record(line, 12m, 10m, UnitType.Unit, Now.AddMinutes(30), "dueno");

        Assert.Equal(((decimal?)12m, (decimal?)12m, (DateTimeOffset?)Now, "cajero"),
            (line.CountedQuantity, line.SystemQuantity, line.CountedAt, line.CountedBy));
        Assert.Equal(0m, line.Difference);
        Assert.Equal(10m, line.ResultingQuantity(10m));
        Assert.Equal(1, count.CountedLineCount);
    }

    [Fact]
    public void Record_ShouldTakeTheSystemAgain_WhenTheItemIsCountedFromScratch()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());
        count.Record(line, 0m, -2m, UnitType.Unit, Now, "cajero");

        // One more sale: cleared and counted again, it is measured against the system of then.
        count.Record(line, null, -3m, UnitType.Unit, Now.AddMinutes(30), "dueno");
        count.Record(line, 0m, -3m, UnitType.Unit, Now.AddMinutes(31), "dueno");

        Assert.Equal(((decimal?)0m, (decimal?)-3m, (DateTimeOffset?)Now.AddMinutes(31), "dueno"),
            (line.CountedQuantity, line.SystemQuantity, line.CountedAt, line.CountedBy));
        Assert.Equal(3m, line.Difference);
        Assert.Equal(0m, line.ResultingQuantity(-3m));
    }

    [Fact]
    public void Record_ShouldOnlyChangeTheNote_WhenTheQuantityIsTheSame()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());
        count.Record(line, 8m, 10m, UnitType.Unit, Now, "cajero");

        count.Record(line, 8m, 7m, UnitType.Unit, Now.AddMinutes(5), "dueno", "Detrás del refrigerador");

        Assert.Equal(((decimal?)10m, (DateTimeOffset?)Now, "cajero", "Detrás del refrigerador"),
            (line.SystemQuantity, line.CountedAt, line.CountedBy, line.Notes));
        Assert.Equal(3, count.Revision);
    }

    [Fact]
    public void Record_WithoutQuantity_ShouldLeaveItUncounted()
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());
        count.Record(line, 8m, 10m, UnitType.Unit, Now, "cajero");

        count.Record(line, null, 10m, UnitType.Unit, Now, "cajero");

        Assert.False(line.IsCounted);
        Assert.Equal(((decimal?)null, (DateTimeOffset?)null, (string?)null), (line.SystemQuantity, line.CountedAt, line.CountedBy));
        Assert.Equal(0, count.CountedLineCount);
    }

    [Theory]
    [InlineData(0, UnitType.Unit)]
    [InlineData(12, UnitType.Pack)]
    [InlineData(1.255, UnitType.Kg)]
    [InlineData(9999999, UnitType.Liter)]
    public void Record_ShouldTakeZeroAndValidQuantities(decimal quantity, UnitType unit)
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());

        count.Record(line, quantity, 3m, unit, Now, "cajero");

        Assert.Equal(quantity, line.CountedQuantity);
    }

    [Theory]
    [InlineData(-1, UnitType.Kg, "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.")]
    [InlineData(1.2345, UnitType.Kg, "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.")]
    [InlineData(10000000, UnitType.Kg, "La cantidad contada va de 0 a 9,999,999, con máximo 3 decimales.")]
    [InlineData(2.5, UnitType.Unit, "La cantidad contada va de 0 a 9,999,999, en piezas enteras.")]
    public void Record_ShouldRefuseQuantitiesItsUnitDoesNotTake(decimal quantity, UnitType unit, string message)
    {
        var count = NewCount();
        var line = count.AddLine(Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => count.Record(line, quantity, 3m, unit, Now, "cajero"));

        Assert.Equal(message, error.Message);
        Assert.False(line.IsCounted);
    }

    [Fact]
    public void Record_ShouldRefuseALineOfAnotherCount()
    {
        var count = NewCount();
        var other = NewCount().AddLine(Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => count.Record(other, 1m, 0m, UnitType.Unit, Now, "cajero"));

        Assert.Equal("El artículo no es de este conteo.", error.Message);
    }

    [Fact]
    public void Post_ShouldCloseIt_WithItsAdjustments()
    {
        var count = NewCount();
        count.Record(count.AddLine(Guid.NewGuid()), 4m, 2m, UnitType.Unit, Now, "cajero");
        var increase = Guid.NewGuid();

        count.Post(Now.AddHours(1), "dueno", increase, null);

        Assert.Equal((InventoryCountStatus.Posted, (DateTimeOffset?)Now.AddHours(1), "dueno", (Guid?)increase, (Guid?)null),
            (count.Status, count.PostedAt, count.PostedBy, count.IncreaseMovementId, count.DecreaseMovementId));
        Assert.False(count.IsOpen);
    }

    [Fact]
    public void Post_ShouldNeedSomethingCounted()
    {
        var count = NewCount();
        count.AddLine(Guid.NewGuid());

        var error = Assert.Throws<DomainException>(() => count.Post(Now, "dueno", null, null));

        Assert.Equal(InventoryCount.NothingCountedMessage, error.Message);
        Assert.True(count.IsOpen);
    }

    [Fact]
    public void Cancel_ShouldCloseIt_WithItsReason()
    {
        var count = NewCount();

        count.Cancel(Now, "dueno", "  Se contó en la ubicación equivocada ");

        Assert.Equal((InventoryCountStatus.Cancelled, (DateTimeOffset?)Now, "dueno", "Se contó en la ubicación equivocada"),
            (count.Status, count.CancelledAt, count.CancelledBy, count.CancelReason));
    }

    [Fact]
    public void AClosedCount_ShouldNotChange()
    {
        var posted = NewCount();
        var line = posted.AddLine(Guid.NewGuid());
        posted.Record(line, 1m, 1m, UnitType.Unit, Now, "cajero");
        posted.Post(Now, "dueno", null, null);
        var cancelled = NewCount();
        var open = cancelled.AddLine(Guid.NewGuid());
        cancelled.Cancel(Now, "dueno");

        Assert.All(
            new Action[]
            {
                () => posted.AddLine(Guid.NewGuid()),
                () => posted.Record(line, 2m, 1m, UnitType.Unit, Now, "cajero"),
                () => posted.Post(Now, "dueno", null, null),
                () => posted.Cancel(Now, "dueno"),
                () => cancelled.Record(open, 2m, 1m, UnitType.Unit, Now, "cajero"),
                () => cancelled.Post(Now, "dueno", null, null),
                () => cancelled.Cancel(Now, "dueno")
            },
            change => Assert.Equal(InventoryCount.NotOpenMessage, Assert.Throws<DomainException>(change).Message));
        Assert.Equal(1m, line.CountedQuantity);
    }

    private static InventoryCount NewCount() => new(Tenant, "CNT-20261021-0001", Location, Now, "dueno");
}
