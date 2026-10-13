using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Commands;

/// <summary>Opens a drawer for the current cashier at a branch. Each cashier has at most one open drawer.</summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record OpenCashSessionCommand : IRequest<Result<CashSessionDto>>
{
    public decimal OpeningAmount { get; init; }
    public string? Notes { get; init; }

    /// <summary>Branch of the drawer; the default branch when omitted.</summary>
    public Guid? BranchId { get; init; }

    /// <summary>Before branches existed the drawer named a stock location: its branch is used.</summary>
    public Guid? InventoryLocationId { get; init; }
}

public class OpenCashSessionCommandValidator : AbstractValidator<OpenCashSessionCommand>
{
    public OpenCashSessionCommandValidator()
    {
        RuleFor(x => x.OpeningAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class OpenCashSessionCommandHandler : IRequestHandler<OpenCashSessionCommand, Result<CashSessionDto>>
{
    public const string AlreadyOpenMessage = "Ya tienes una caja abierta. Ciérrala antes de abrir otra.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public OpenCashSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CashSessionDto>> Handle(OpenCashSessionCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, Domain.Orders.Enums.OrderType.Retail) &&
            !TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, Domain.Orders.Enums.OrderType.Service))
        {
            return Result.Failure<CashSessionDto>("El negocio no tiene módulos comerciales habilitados.");
        }

        var userId = CashSessionMapping.ParseUserId(_currentUserService.UserId);
        if (userId is null)
            return Result.Failure<CashSessionDto>("No se pudo identificar a quién abre la caja.");

        var hasOpenSession = await _context.CashSessions
            .AnyAsync(x => x.Status == CashSessionStatus.Open && x.OpenedByUserId == userId, cancellationToken);
        if (hasOpenSession)
            return Result.Failure<CashSessionDto>(AlreadyOpenMessage);

        var place = await ResolvePlaceAsync(request, cancellationToken);
        if (place.IsFailure)
            return Result.Failure<CashSessionDto>(place.Error!);
        var (branchId, branchName, locationId) = place.Value;

        var session = new CashSession(
            tenantId: _tenantService.TenantId,
            openedAt: _dateTimeProvider.UtcNow,
            openedBy: _currentUserService.UserName ?? "system",
            openingAmount: request.OpeningAmount,
            notes: request.Notes,
            openedByUserId: userId,
            inventoryLocationId: locationId,
            branchId: branchId);

        _context.CashSessions.Add(session);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request of the same cashier opened a drawer first (unique index per open cashier).
            return Result.Failure<CashSessionDto>(AlreadyOpenMessage);
        }

        var locationName = locationId is { } stockLocationId
            ? await _context.InventoryLocations.Where(x => x.Id == stockLocationId).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var people = await CashPeople.ResolveAsync(_context, [session.OpenedBy], [session.OpenedByUserId], cancellationToken);
        return Result.Success(CashSessionMapping.ToDto(session, userId, locationName, branchName, people));
    }

    /// <summary>The branch of the drawer (asked for, the branch of the location asked for, or the default) and its stock location.</summary>
    private async Task<Result<(Guid? BranchId, string? BranchName, Guid? LocationId)>> ResolvePlaceAsync(
        OpenCashSessionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        Guid? branchId = request.BranchId;
        Guid? locationId = null;
        if (branchId is null && request.InventoryLocationId is { } requestedLocationId)
        {
            var location = await _context.InventoryLocations
                .Where(x => x.Id == requestedLocationId && x.IsActive)
                .Select(x => new { x.Id, x.BranchId })
                .FirstOrDefaultAsync(cancellationToken);
            if (location is null)
                return Result.Failure<(Guid?, string?, Guid?)>(BranchRules.InvalidBranchMessage);
            locationId = location.Id;
            branchId = location.BranchId;
        }

        branchId ??= await BranchRules.DefaultBranchIdAsync(_context, tenantId, cancellationToken);
        string? branchName = null;
        if (branchId is { } id)
        {
            branchName = await _context.Branches
                .Where(x => x.Id == id && x.IsActive)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (branchName is null)
                return Result.Failure<(Guid?, string?, Guid?)>(BranchRules.InvalidBranchMessage);
        }

        locationId ??= await BranchRules.StockLocationIdAsync(_context, tenantId, branchId, cancellationToken);
        return Result.Success<(Guid?, string?, Guid?)>((branchId, branchName, locationId));
    }
}
