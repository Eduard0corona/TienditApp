using System.Linq.Expressions;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;

namespace FoodParkDigital.Application.Branches;

/// <summary>A branch of the business. Same fields the branches had when they were stock locations.</summary>
public class BranchDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public string? Description { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    /// <summary>Whether customers come to it or it only delivers, keeping its address private.</summary>
    public BranchServiceMode ServiceMode { get; set; }
    /// <summary>Where it is on the map (Fase 24G), with <see cref="Longitude"/>; both null until the owner sets it.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public static Expression<Func<Branch, BranchDto>> Projection { get; } = x => new BranchDto
    {
        Id = x.Id,
        Name = x.Name,
        Code = x.Code,
        Description = x.Description,
        AddressLine1 = x.AddressLine1,
        AddressLine2 = x.AddressLine2,
        City = x.City,
        State = x.State,
        Country = x.Country,
        PostalCode = x.PostalCode,
        ContactName = x.ContactName,
        ContactPhone = x.ContactPhone,
        IsDefault = x.IsDefault,
        IsActive = x.IsActive,
        ServiceMode = x.ServiceMode,
        Latitude = x.Latitude,
        Longitude = x.Longitude,
        CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt
    };

    private static readonly Func<Branch, BranchDto> FromEntityCompiled = Projection.Compile();

    public static BranchDto FromEntity(Branch branch) => FromEntityCompiled(branch);
}
