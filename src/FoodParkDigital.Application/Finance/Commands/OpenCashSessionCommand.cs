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
            return Result.Failure<CashSessionDto>("El tenant no tiene módulos comerciales habilitados.");
        }

        var userId = CashSessionMapping.ParseUserId(_currentUserService.UserId);
        if (userId is null)
            return Result.Failure<CashSessionDto>("No se pudo identificar a quién abre la caja.");

        var hasOpenSession = await _context.CashSessions
            .AnyAsync(x => x.Status == CashSessionStatus.Open && x.OpenedByUserId == userId, cancellationToken);
        if (hasOpenSession)
            return Result.Failure<CashSessionDto>(AlreadyOpenMessage);

        var location = request.InventoryLocationId is { } locationId
            ? await _context.InventoryLocations
                .Where(x => x.Id == locationId && x.IsActive)
                .Select(x => new { x.Id, x.Name })
                .FirstOrDefaultAsync(cancellationToken)
            : await _context.InventoryLocations
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .Select(x => new { x.Id, x.Name })
                .FirstOrDefaultAsync(cancellationToken);
        if (request.InventoryLocationId is not null && location is null)
            return Result.Failure<CashSessionDto>("La sucursal seleccionada no es válida.");

        var session = new CashSession(
            tenantId: _tenantService.TenantId,
            openedAt: _dateTimeProvider.UtcNow,
            openedBy: _currentUserService.UserName ?? "system",
            openingAmount: request.OpeningAmount,
            notes: request.Notes,
            openedByUserId: userId,
            inventoryLocationId: location?.Id);

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

        return Result.Success(CashSessionMapping.ToDto(session, userId, location?.Name));
    }
}
