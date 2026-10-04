using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Finance.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Rules;

/// <summary>Which branch a sale or a drawer belongs to, and where the stock of a branch is.</summary>
public static class BranchRules
{
    public const string InvalidBranchMessage = "La sucursal seleccionada no es válida.";

    /// <summary>
    /// Branch of a new sale: the one asked for (it must be an active branch of the business), else the branch of
    /// the cashier's open drawer, else the default branch. Null only for a business without branches.
    /// </summary>
    public static async Task<Result<Guid?>> ResolveForSaleAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid? requestedBranchId,
        Guid? cashierUserId,
        CancellationToken cancellationToken)
    {
        if (requestedBranchId is { } branchId)
        {
            return await IsActiveBranchAsync(context, tenantId, branchId, cancellationToken)
                ? Result.Success<Guid?>(branchId)
                : Result.Failure<Guid?>(InvalidBranchMessage);
        }

        if (cashierUserId is { } userId)
        {
            var drawerBranchId = await context.CashSessions
                .Where(x => x.TenantId == tenantId
                    && x.Status == CashSessionStatus.Open
                    && x.OpenedByUserId == userId
                    && x.BranchId != null)
                .Select(x => x.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            if (drawerBranchId is not null)
                return Result.Success(drawerBranchId);
        }

        return Result.Success(await DefaultBranchIdAsync(context, tenantId, cancellationToken));
    }

    public static Task<bool> IsActiveBranchAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid branchId,
        CancellationToken cancellationToken)
        => context.Branches.AnyAsync(x => x.Id == branchId && x.TenantId == tenantId && x.IsActive, cancellationToken);

    public static Task<Guid?> DefaultBranchIdAsync(IApplicationDbContext context, Guid tenantId, CancellationToken cancellationToken)
        => context.Branches
            .Where(x => x.TenantId == tenantId && x.IsDefault)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Where a branch takes stock from and puts it back: its default location, else its first active one. A branch
    /// without locations (and a sale without branch) uses the business's default location.
    /// </summary>
    public static async Task<Guid?> StockLocationIdAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        if (branchId is { } id)
        {
            var branchLocationId = await context.InventoryLocations
                .Where(x => x.TenantId == tenantId && x.BranchId == id && x.IsActive)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (branchLocationId is not null)
                return branchLocationId;
        }

        return await context.InventoryLocations
            .Where(x => x.TenantId == tenantId && x.IsActive && x.IsDefault)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
