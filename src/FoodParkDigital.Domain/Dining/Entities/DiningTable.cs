using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Dining.Entities;

/// <summary>A table (or bar seat) where a tab can be opened. A table has one open tab at most.</summary>
public class DiningTable : AuditableEntity, ITenantScoped
{
    public const int MaxNameLength = 30;
    public const int MaxAreaLength = 50;
    public const int MaxSeats = 100;

    private DiningTable() { }

    public DiningTable(Guid tenantId, string name, string? area, int? seats, int sortOrder)
    {
        TenantId = tenantId;
        Update(name, area, seats, sortOrder, isActive: true);
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>Where it is: "Terraza", "Salón", "Barra".</summary>
    public string? Area { get; private set; }
    public int? Seats { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? area, int? seats, int sortOrder, bool isActive)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
            throw new DomainException($"El nombre de la mesa es obligatorio y tiene máximo {MaxNameLength} caracteres.");
        var trimmedArea = string.IsNullOrWhiteSpace(area) ? null : area.Trim();
        if (trimmedArea?.Length > MaxAreaLength)
            throw new DomainException($"El área tiene máximo {MaxAreaLength} caracteres.");
        if (seats is < 1 or > MaxSeats)
            throw new DomainException($"Los lugares van de 1 a {MaxSeats}.");

        Name = trimmedName;
        Area = trimmedArea;
        Seats = seats;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
