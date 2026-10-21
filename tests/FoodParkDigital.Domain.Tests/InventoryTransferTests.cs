using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Transferencias (Fase 27B, ADR 0038): a transfer takes items from one location and puts them in another at once, with
/// its folio, its two movements and what the items were worth; made, it does not change.
/// </summary>
public class InventoryTransferTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Warehouse = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void New_ShouldKeepWhereFromAndToWhatItsMovementsAndWhoMadeIt()
    {
        var id = Guid.NewGuid();
        var key = Guid.NewGuid();
        var (outMovement, inMovement) = (Guid.NewGuid(), Guid.NewGuid());

        var transfer = new InventoryTransfer(
            id, Tenant, "TRF-20261021-0001", key, Warehouse, Branch, Now, "  dueno@tienda.mx ", outMovement, inMovement, 3, 412.5m,
            "  Para el fin de semana  ");

        Assert.Equal((id, Tenant, "TRF-20261021-0001", key), (transfer.Id, transfer.TenantId, transfer.TransferNumber, transfer.IdempotencyKey));
        Assert.Equal((Warehouse, Branch, Now, "dueno@tienda.mx"), (transfer.FromLocationId, transfer.ToLocationId, transfer.TransferredAt, transfer.TransferredBy));
        Assert.Equal((outMovement, inMovement, 3, 412.5m, "Para el fin de semana"),
            (transfer.OutMovementId, transfer.InMovementId, transfer.LineCount, transfer.TotalCost, transfer.Notes));
    }

    [Fact]
    public void New_ShouldNeedTwoDifferentLocations()
    {
        var same = Assert.Throws<DomainException>(() => New(from: Warehouse, to: Warehouse));
        var missing = Assert.Throws<DomainException>(() => New(from: Warehouse, to: Guid.Empty));

        Assert.Equal(InventoryTransfer.SameLocationMessage, same.Message);
        Assert.Equal("Elige la ubicación de origen y la de destino.", missing.Message);
    }

    [Theory]
    [InlineData(0, "La transferencia necesita al menos un artículo.")]
    [InlineData(201, InventoryTransfer.TooManyLinesMessage)]
    public void New_ShouldTakeFromOneTo200Items(int lineCount, string message)
    {
        Assert.Equal(message, Assert.Throws<DomainException>(() => New(lineCount: lineCount)).Message);
        Assert.Equal(200, New(lineCount: 200).LineCount);
    }

    [Fact]
    public void New_ShouldNeedItsTwoMovements_ANonNegativeValue_AndItsKey()
    {
        var movement = Guid.NewGuid();

        Assert.Equal("La transferencia necesita su salida y su entrada.",
            Assert.Throws<DomainException>(() => New(outMovement: movement, inMovement: movement)).Message);
        Assert.Equal("El valor de la transferencia no puede ser negativo.", Assert.Throws<DomainException>(() => New(totalCost: -0.01m)).Message);
        Assert.Equal("La transferencia necesita su llave.", Assert.Throws<DomainException>(() => New(key: Guid.Empty)).Message);
        Assert.Equal(0m, New(totalCost: 0m).TotalCost);
    }

    [Fact]
    public void Notes_ShouldBeClippedTo500()
    {
        Assert.Equal(InventoryTransfer.MaxNotesLength, New(notes: new string('x', 600)).Notes!.Length);
        Assert.Null(New(notes: "   ").Notes);
    }

    [Fact]
    public void Folio_ShouldStartAgainEachUtcDay()
    {
        var scope = Folios.InventoryTransferScope(new DateTimeOffset(2026, 10, 21, 20, 0, 0, TimeSpan.FromHours(-6)));

        Assert.Equal("TRF-20261022", scope);
        Assert.Equal("TRF-20261022-0003", Folios.DailyNumber(scope, 3));
    }

    private static InventoryTransfer New(
        Guid? from = null,
        Guid? to = null,
        int lineCount = 1,
        Guid? outMovement = null,
        Guid? inMovement = null,
        decimal totalCost = 10m,
        Guid? key = null,
        string? notes = null)
        => new(Guid.NewGuid(), Tenant, "TRF-20261021-0001", key ?? Guid.NewGuid(), from ?? Warehouse, to ?? Branch, Now, "dueno",
            outMovement ?? Guid.NewGuid(), inMovement ?? Guid.NewGuid(), lineCount, totalCost, notes);
}
