using System.Linq.Expressions;
using FoodParkDigital.Domain.Inventory.Entities;

namespace FoodParkDigital.Application.Inventory.DTOs;

public static class InventoryMappings
{
    /// <summary>The supply as stored; its stock totals are filled by the caller.</summary>
    public static SupplyItemDto ToDto(this SupplyItem supply) => new()
    {
        Id = supply.Id,
        Name = supply.Name,
        Code = supply.Code,
        Sku = supply.Sku,
        Barcode = supply.Barcode,
        Description = supply.Description,
        Brand = supply.Brand,
        Model = supply.Model,
        CategoryName = supply.CategoryName,
        Unit = supply.Unit,
        DefaultCost = supply.DefaultCost,
        LastCost = supply.LastCost,
        AverageCost = supply.AverageCost,
        MinStock = supply.MinStock,
        MaxStock = supply.MaxStock,
        ReorderPoint = supply.ReorderPoint,
        TrackBatches = supply.TrackBatches,
        TrackExpiration = supply.TrackExpiration,
        IsPerishable = supply.IsPerishable,
        IsCritical = supply.IsCritical,
        TaxRate = supply.TaxRate,
        Notes = supply.Notes,
        IsActive = supply.IsActive,
        CreatedAt = supply.CreatedAt,
        UpdatedAt = supply.UpdatedAt
    };

    /// <summary>The location as stored; the name of its branch is filled by the caller.</summary>
    public static InventoryLocationDto ToDto(this InventoryLocation location) => AsDtoCompiled(location);

    public static readonly Expression<Func<InventoryLocation, InventoryLocationDto>> AsDto = location => new InventoryLocationDto
    {
        Id = location.Id,
        Name = location.Name,
        Code = location.Code,
        Description = location.Description,
        AddressLine1 = location.AddressLine1,
        AddressLine2 = location.AddressLine2,
        City = location.City,
        State = location.State,
        Country = location.Country,
        PostalCode = location.PostalCode,
        ContactName = location.ContactName,
        ContactPhone = location.ContactPhone,
        IsDefault = location.IsDefault,
        IsActive = location.IsActive,
        BranchId = location.BranchId,
        CreatedAt = location.CreatedAt,
        UpdatedAt = location.UpdatedAt
    };

    private static readonly Func<InventoryLocation, InventoryLocationDto> AsDtoCompiled = AsDto.Compile();

    /// <summary>The movement with its lines; a line names its supply and location when they are loaded.</summary>
    public static InventoryMovementDto ToDto(this InventoryMovement movement) => new()
    {
        Id = movement.Id,
        MovementNumber = movement.MovementNumber,
        MovementType = movement.MovementType,
        MovementReason = movement.MovementReason,
        MovementDate = movement.MovementDate,
        ReferenceNumber = movement.ReferenceNumber,
        ExternalReference = movement.ExternalReference,
        CounterpartyName = movement.CounterpartyName,
        DocumentNumber = movement.DocumentNumber,
        Notes = movement.Notes,
        TotalAmount = movement.TotalAmount,
        CreatedAt = movement.CreatedAt,
        Lines = movement.Lines.Select(line => new InventoryMovementLineDto
        {
            Id = line.Id,
            SupplyItemId = line.SupplyItemId,
            SupplyItemName = line.SupplyItem?.Name!,
            InventoryLocationId = line.InventoryLocationId,
            InventoryLocationName = line.InventoryLocation?.Name!,
            Quantity = line.Quantity,
            UnitCost = line.UnitCost,
            TotalCost = line.TotalCost,
            QuantityBefore = line.QuantityBefore,
            QuantityAfter = line.QuantityAfter,
            LotNumber = line.LotNumber,
            ExpirationDate = line.ExpirationDate,
            Notes = line.Notes
        }).ToList()
    };
}
