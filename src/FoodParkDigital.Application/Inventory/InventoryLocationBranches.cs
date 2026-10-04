using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Inventory.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory;

/// <summary>The branch of a stock location: checked when it is assigned and named when it is shown.</summary>
internal static class InventoryLocationBranches
{
    public static async Task<string?> ValidateAsync(IApplicationDbContext context, Guid tenantId, Guid? branchId, CancellationToken cancellationToken)
        => branchId is { } id && !await BranchRules.IsActiveBranchAsync(context, tenantId, id, cancellationToken)
            ? BranchRules.InvalidBranchMessage
            : null;

    public static async Task<List<InventoryLocationDto>> NameAsync(
        IApplicationDbContext context,
        List<InventoryLocationDto> locations,
        CancellationToken cancellationToken)
    {
        var branchIds = locations.Where(x => x.BranchId.HasValue).Select(x => x.BranchId!.Value).Distinct().ToList();
        if (branchIds.Count == 0)
            return locations;

        var names = await context.Branches
            .AsNoTracking()
            .Where(x => branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        foreach (var location in locations)
            location.BranchName = location.BranchId is { } id && names.TryGetValue(id, out var name) ? name : null;
        return locations;
    }
}
