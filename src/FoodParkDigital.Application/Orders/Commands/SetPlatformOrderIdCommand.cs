using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// Writes, corrects or clears the platform's long id of an order of Rappi, Uber Eats or DiDi Food (Fase 22I, ADR 0041), at
/// any status: whoever takes the orders, as when capturing it. Empty clears it. Another order of the platform that still
/// counts with the same long id is a warning the business confirms. The order's activity keeps the one before (its
/// domain event, written by the outbox). Two corrections of the same order at once: the second answers 409
/// <c>concurrency_conflict</c> (the order's version), so its activity never tells a change that did not stay.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders)]
public record SetPlatformOrderIdCommand(Guid OrderId, string? ExternalOrderId, bool ConfirmPossibleDuplicate = false)
    : IRequest<Result>;

public class SetPlatformOrderIdCommandValidator : AbstractValidator<SetPlatformOrderIdCommand>
{
    public SetPlatformOrderIdCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Recarga e intenta de nuevo.");
        RuleFor(x => x.ExternalOrderId)
            .Must(id => string.IsNullOrWhiteSpace(id) || PlatformOrderId.TryNormalize(id, out _))
            .WithMessage(PlatformOrderId.InvalidMessage);
    }
}

public class SetPlatformOrderIdCommandHandler : IRequestHandler<SetPlatformOrderIdCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public SetPlatformOrderIdCommandHandler(
        IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<Result> Handle(SetPlatformOrderIdCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var order = await _context.Orders
            .Include(o => o.PlatformDetail)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.TenantId == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Order), request.OrderId);

        try
        {
            var longId = PlatformOrderId.FromOptional(request.ExternalOrderId);
            if (longId is not null && longId.Value != order.PlatformDetail?.ExternalOrderId && !request.ConfirmPossibleDuplicate
                && await PlatformOrderPlacement.HasLongIdDuplicateAsync(
                    _context, tenantId, order.Channel, longId, order.Id, cancellationToken))
            {
                return Result.Failure(PlatformOrderErrors.LongIdInUse);
            }

            if (!order.ChangePlatformOrderId(longId, _clock.UtcNow))
                return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
