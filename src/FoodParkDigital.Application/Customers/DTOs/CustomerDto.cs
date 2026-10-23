using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Customers.Entities;

namespace FoodParkDigital.Application.Customers.DTOs;

public class CustomerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsPublicGeneral { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    /// <summary>How much the business lets them owe (Fase 26A); null when they are not sold on credit.</summary>
    public decimal? CreditLimit { get; set; }
    /// <summary>Their own term to pay what they owe (Fase 26C); null when the business's applies.</summary>
    public int? CreditTermDays { get; set; }
    /// <summary>What their invoices go to (Fase 28A); null when the business has not captured it.</summary>
    public CfdiReceiverDto? FiscalData { get; set; }
    /// <summary>Only in the detail of one customer (the list leaves it empty).</summary>
    public List<CustomerAddressDto> Addresses { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The customer with its addresses, the default one first.</summary>
    public static CustomerDto From(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Phone = customer.Phone,
        Email = customer.Email,
        IsPublicGeneral = customer.IsPublicGeneral,
        IsActive = customer.IsActive,
        Notes = customer.Notes,
        CreditLimit = customer.CreditLimit,
        CreditTermDays = customer.CreditTermDays,
        FiscalData = customer.FiscalData is null ? null : CfdiReceiverDto.From(customer.FiscalData),
        Addresses = customer.Addresses
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .Select(CustomerAddressDto.From)
            .ToList(),
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt
    };
}

public class CustomerAddressDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = null!;
    public string AddressLine { get; set; } = null!;
    public string? References { get; set; }
    public bool IsDefault { get; set; }

    public static CustomerAddressDto From(CustomerAddress address) => new()
    {
        Id = address.Id,
        Label = address.Label,
        AddressLine = address.AddressLine,
        References = address.References,
        IsDefault = address.IsDefault
    };
}
