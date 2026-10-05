using FoodParkDigital.Domain.Purchasing.Entities;

namespace FoodParkDigital.Application.Purchasing.DTOs;

public static class SupplierMappings
{
    public static SupplierDto ToDto(this Supplier supplier) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        ContactName = supplier.ContactName,
        Phone = supplier.Phone,
        Email = supplier.Email,
        TaxId = supplier.TaxId,
        Address = supplier.Address,
        Notes = supplier.Notes,
        IsActive = supplier.IsActive
    };
}
