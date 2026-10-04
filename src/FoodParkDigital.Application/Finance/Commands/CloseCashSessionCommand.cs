using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance.Commands;

/// <summary>
/// Closes a drawer with the counted cash. The expected amount and the difference are kept for the shift
/// history; a difference needs a note. A cashier closes their own drawer; the owner or an admin can close any.
/// </summary>
[RequiresPermission(TenantPermission.ManageCash)]
public record CloseCashSessionCommand : IRequest<Result<CashSessionDto>>
{
    public Guid CashSessionId { get; init; }
    public decimal ClosingAmount { get; init; }
    public string? Notes { get; init; }

    /// <summary>Optional count by denomination; when sent it must add up to <see cref="ClosingAmount"/>.</summary>
    public List<CashCountLineDto>? CashCount { get; init; }
}

public class CloseCashSessionCommandValidator : AbstractValidator<CloseCashSessionCommand>
{
    public CloseCashSessionCommandValidator()
    {
        RuleFor(x => x.CashSessionId).NotEmpty();
        RuleFor(x => x.ClosingAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.CashCount!)
            .Must(lines => lines.Count <= 30)
            .WithMessage("El conteo admite como máximo 30 denominaciones.")
            .When(x => x.CashCount is not null);
        RuleForEach(x => x.CashCount).ChildRules(line =>
        {
            line.RuleFor(l => l.Denomination).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
            line.RuleFor(l => l.Quantity).InclusiveBetween(0, 100_000);
        });
        RuleFor(x => x)
            .Must(x => x.CashCount is null || x.CashCount.Count == 0
                || x.CashCount.Sum(l => l.Denomination * l.Quantity) == x.ClosingAmount)
            .WithMessage("El conteo por denominación no coincide con el monto contado.");
    }
}

public class CloseCashSessionCommandHandler : IRequestHandler<CloseCashSessionCommand, Result<CashSessionDto>>
{
    public const string NotYourDrawerMessage = "Solo quien abrió la caja o el administrador del negocio puede cerrarla.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CloseCashSessionCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CashSessionDto>> Handle(CloseCashSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _context.CashSessions
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(x => x.Id == request.CashSessionId, cancellationToken)
            ?? throw new EntityNotFoundException("CashSession", request.CashSessionId);

        var userId = CashSessionMapping.ParseUserId(_currentUserService.UserId);
        // Drawers from before per-cashier sessions have no owner: anyone with access to the cash closes them.
        var canClose = session.OpenedByUserId is null
            || session.IsOwnedBy(userId)
            || TenantManagementRules.CanManageTenant(_currentUserService);
        if (!canClose)
            return Result.Failure<CashSessionDto>(NotYourDrawerMessage);

        try
        {
            session.Close(
                closedAt: _dateTimeProvider.UtcNow,
                closedBy: _currentUserService.UserName ?? "system",
                closingAmount: request.ClosingAmount,
                notes: request.Notes,
                cashCountJson: CashSessionMapping.WriteCashCount(request.CashCount));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CashSessionDto>(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var locationName = await LocationNameAsync(session.InventoryLocationId, cancellationToken);
        return Result.Success(CashSessionMapping.ToDto(session, userId, locationName));
    }

    private Task<string?> LocationNameAsync(Guid? locationId, CancellationToken cancellationToken)
        => locationId is null
            ? Task.FromResult<string?>(null)
            : _context.InventoryLocations
                .Where(x => x.Id == locationId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
}
