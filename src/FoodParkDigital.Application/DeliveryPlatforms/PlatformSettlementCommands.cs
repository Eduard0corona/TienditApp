using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>Why a change of a settlement is refused.</summary>
public static class PlatformSettlementErrors
{
    /// <summary>Someone changed the settlement since the person loaded it: the same text as any concurrent save.</summary>
    public const string Stale = DeliveryPlatformErrors.Stale;

    public const string OrderNotOfThePlatform = "Uno de los pedidos no es de esta plataforma o ya no existe.";

    public const string OrderTaken = "Otra liquidación ya tiene uno de los pedidos. Recarga e intenta de nuevo.";

    /// <summary>A null in the list of adjustments.</summary>
    public const string EmptyAdjustment = "La lista trae un ajuste vacío: cada ajuste va con su motivo y su importe.";

    public const string ExpectedNetsChanged =
        "Cambió lo que se espera de un pedido desde que guardaste (por una devolución, por ejemplo). Revisa la liquidación y ciérrala otra vez.";

    public static string OpenExists(OrderChannel channel)
        => $"Ya hay una liquidación abierta de {OrderChannelRules.DisplayName(channel)}: termínala o anúlala antes de abrir otra.";

    /// <summary>Someone else's save in between: the API answers 409 and the person reloads.</summary>
    public static bool IsConflict(string? error) => error is Stale or OrderTaken or ExpectedNetsChanged;
}

/// <summary>Opens a settlement of a platform's deposit with the orders it pays and the adjustments that explain the rest.</summary>
[RequiresTenantManager]
public record CreatePlatformSettlementCommand(
    OrderChannel Channel,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly DepositedOn,
    decimal DepositedAmount,
    string? Reference,
    string? Notes,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<PlatformSettlementAdjustmentInput> Adjustments) : IRequest<Result<PlatformSettlementDetailDto>>;

/// <summary>Replaces an open settlement whole: its deposit, its orders and its adjustments, with the revision the person saw.</summary>
[RequiresTenantManager]
public record UpdatePlatformSettlementCommand(
    Guid Id,
    int Revision,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    DateOnly DepositedOn,
    decimal DepositedAmount,
    string? Reference,
    string? Notes,
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<PlatformSettlementAdjustmentInput> Adjustments) : IRequest<Result<PlatformSettlementDetailDto>>;

/// <summary>Closes a settlement once every peso of its deposit is explained.</summary>
[RequiresTenantManager]
public record ClosePlatformSettlementCommand(Guid Id, int Revision) : IRequest<Result<PlatformSettlementDetailDto>>;

/// <summary>Voids a settlement with a reason: it stays as history and its orders are pending again.</summary>
[RequiresTenantManager]
public record VoidPlatformSettlementCommand(Guid Id, int Revision, string? Reason) : IRequest<Result<PlatformSettlementDetailDto>>;

internal static class PlatformSettlementRules
{
    public const string RepeatedOrderMessage = PlatformSettlement.RepeatedOrderMessage;

    /// <summary>What a request can say about its orders and adjustments before reading anything.</summary>
    public static void AddListRules<T>(
        AbstractValidator<T> validator,
        Func<T, IReadOnlyList<Guid>> orderIds,
        Func<T, IReadOnlyList<PlatformSettlementAdjustmentInput>> adjustments)
    {
        validator.RuleFor(x => orderIds(x)).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(PlatformSettlement.TooManyOrdersMessage)
            .Must(ids => ids.Count <= PlatformSettlement.MaxOrders).WithMessage(PlatformSettlement.TooManyOrdersMessage)
            .Must(ids => ids.All(id => id != Guid.Empty)).WithMessage(PlatformSettlementErrors.OrderNotOfThePlatform)
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage(RepeatedOrderMessage)
            .OverridePropertyName("OrderIds");
        validator.RuleFor(x => adjustments(x)).Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(PlatformSettlement.TooManyAdjustmentsMessage)
            .Must(items => items.Count <= PlatformSettlement.MaxAdjustments).WithMessage(PlatformSettlement.TooManyAdjustmentsMessage)
            .Must(items => items.All(item => item is not null)).WithMessage(PlatformSettlementErrors.EmptyAdjustment)
            .OverridePropertyName("Adjustments");
    }
}

public class CreatePlatformSettlementCommandValidator : AbstractValidator<CreatePlatformSettlementCommand>
{
    public CreatePlatformSettlementCommandValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
        PlatformSettlementRules.AddListRules(this, x => x.OrderIds, x => x.Adjustments);
    }
}

public class UpdatePlatformSettlementCommandValidator : AbstractValidator<UpdatePlatformSettlementCommand>
{
    public UpdatePlatformSettlementCommandValidator()
    {
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(1);
        PlatformSettlementRules.AddListRules(this, x => x.OrderIds, x => x.Adjustments);
    }
}

public class ClosePlatformSettlementCommandValidator : AbstractValidator<ClosePlatformSettlementCommand>
{
    public ClosePlatformSettlementCommandValidator()
    {
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(1);
    }
}

public class VoidPlatformSettlementCommandValidator : AbstractValidator<VoidPlatformSettlementCommand>
{
    public VoidPlatformSettlementCommandValidator()
    {
        RuleFor(x => x.Revision).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Reason).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(PlatformSettlement.VoidReasonRequiredMessage)
            .Must(reason => reason!.Trim().Length <= PlatformSettlement.MaxVoidReasonLength)
            .WithMessage(PlatformSettlement.VoidReasonTooLongMessage);
    }
}

public class CreatePlatformSettlementCommandHandler
    : IRequestHandler<CreatePlatformSettlementCommand, Result<PlatformSettlementDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public CreatePlatformSettlementCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<Result<PlatformSettlementDetailDto>> Handle(CreatePlatformSettlementCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var zone = await PlatformSettlementReader.ZoneAsync(_context, tenantId, cancellationToken);
        // One deposit at a time: two open settlements of a platform would fight over the same orders.
        if (await _context.PlatformSettlements.AnyAsync(
                s => s.Channel == request.Channel && s.Status == PlatformSettlementStatus.Open, cancellationToken))
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.OpenExists(request.Channel));

        var chosen = await PlatformSettlementReader.ChosenAsync(
            _context, request.Channel, request.OrderIds, settlementId: null, zone, cancellationToken);
        if (chosen.Error is { } error)
            return Result.Failure<PlatformSettlementDetailDto>(error);

        PlatformSettlement settlement;
        try
        {
            settlement = new PlatformSettlement(
                tenantId,
                request.Channel,
                new PlatformSettlementDeposit(
                    request.PeriodFrom, request.PeriodTo, request.DepositedOn, request.DepositedAmount, request.Reference, request.Notes),
                chosen.Orders,
                request.Adjustments,
                DailySalesReadModel.LocalToday(zone, _clock.UtcNow));
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformSettlementDetailDto>(ex.Message);
        }

        _context.PlatformSettlements.Add(settlement);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Someone opened a settlement of the platform, or took one of these orders, at the same time (the unique
            // indexes): this one is undone whole, and the person reloads to see the other.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await PlatformSettlementConflicts.OpenedMeanwhileAsync(_context, request.Channel, cancellationToken)
                || await PlatformSettlementConflicts.TakenMeanwhileAsync(_context, request.OrderIds, null, cancellationToken))
                return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);
            throw;
        }

        return Result.Success(await PlatformSettlementReader.DetailAsync(_context, settlement.Id, zone, cancellationToken));
    }
}

public class UpdatePlatformSettlementCommandHandler
    : IRequestHandler<UpdatePlatformSettlementCommand, Result<PlatformSettlementDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public UpdatePlatformSettlementCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<Result<PlatformSettlementDetailDto>> Handle(UpdatePlatformSettlementCommand request, CancellationToken cancellationToken)
    {
        var zone = await PlatformSettlementReader.ZoneAsync(_context, _tenantService.TenantId, cancellationToken);
        var settlement = await PlatformSettlementConflicts.LoadAsync(_context, request.Id, cancellationToken);
        if (settlement.Revision != request.Revision)
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);

        var chosen = await PlatformSettlementReader.ChosenAsync(
            _context, settlement.Channel, request.OrderIds, settlement.Id, zone, cancellationToken);
        if (chosen.Error is { } error)
            return Result.Failure<PlatformSettlementDetailDto>(error);

        try
        {
            settlement.Update(
                new PlatformSettlementDeposit(
                    request.PeriodFrom, request.PeriodTo, request.DepositedOn, request.DepositedAmount, request.Reference, request.Notes),
                chosen.Orders,
                request.Adjustments,
                DailySalesReadModel.LocalToday(zone, _clock.UtcNow));
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformSettlementDetailDto>(ex.Message);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone changed the settlement after it was read here: this save was made over an older revision.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);
        }
        catch (DbUpdateException)
        {
            // Another settlement took one of the orders at the same time (the unique index): undone whole.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await PlatformSettlementConflicts.TakenMeanwhileAsync(_context, request.OrderIds, settlement.Id, cancellationToken))
                return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);
            throw;
        }

        return Result.Success(await PlatformSettlementReader.DetailAsync(_context, settlement.Id, zone, cancellationToken));
    }
}

public class ClosePlatformSettlementCommandHandler
    : IRequestHandler<ClosePlatformSettlementCommand, Result<PlatformSettlementDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public ClosePlatformSettlementCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result<PlatformSettlementDetailDto>> Handle(ClosePlatformSettlementCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var zone = await PlatformSettlementReader.ZoneAsync(_context, tenantId, cancellationToken);
        var settlement = await PlatformSettlementConflicts.LoadAsync(_context, request.Id, cancellationToken);
        if (settlement.Revision != request.Revision)
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);

        try
        {
            // A refund since the last save changes what an order brings: the new figures are saved, and the person
            // reviews them before closing, instead of closing on numbers nobody saw.
            var expectedNets = await PlatformSettlementReader.ExpectedNetsAsync(
                _context, settlement.Orders.Select(line => line.OrderId).ToList(), cancellationToken);
            if (settlement.Recount(expectedNets))
            {
                var saved = await PlatformSettlementConflicts.SaveAsync(_context, cancellationToken);
                return Result.Failure<PlatformSettlementDetailDto>(saved ? PlatformSettlementErrors.ExpectedNetsChanged : PlatformSettlementErrors.Stale);
            }

            settlement.Close(_clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformSettlementDetailDto>(ex.Message);
        }

        // Closing settles its orders for good: who closed what, and with which figures, stays recorded in the same save.
        _context.AuditLogs.Add(PlatformSettlementAudit.Closed(tenantId, settlement, _currentUserService.UserName ?? "system"));
        if (!await PlatformSettlementConflicts.SaveAsync(_context, cancellationToken))
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);

        return Result.Success(await PlatformSettlementReader.DetailAsync(_context, settlement.Id, zone, cancellationToken));
    }
}

public class VoidPlatformSettlementCommandHandler
    : IRequestHandler<VoidPlatformSettlementCommand, Result<PlatformSettlementDetailDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _clock;

    public VoidPlatformSettlementCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _clock = clock;
    }

    public async Task<Result<PlatformSettlementDetailDto>> Handle(VoidPlatformSettlementCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var zone = await PlatformSettlementReader.ZoneAsync(_context, tenantId, cancellationToken);
        var settlement = await PlatformSettlementConflicts.LoadAsync(_context, request.Id, cancellationToken);
        if (settlement.Revision != request.Revision)
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);

        var previousStatus = settlement.Status;
        try
        {
            settlement.Void(request.Reason, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PlatformSettlementDetailDto>(ex.Message);
        }

        _context.AuditLogs.Add(PlatformSettlementAudit.Voided(tenantId, settlement, previousStatus, _currentUserService.UserName ?? "system"));
        if (!await PlatformSettlementConflicts.SaveAsync(_context, cancellationToken))
            return Result.Failure<PlatformSettlementDetailDto>(PlatformSettlementErrors.Stale);

        return Result.Success(await PlatformSettlementReader.DetailAsync(_context, settlement.Id, zone, cancellationToken));
    }
}

/// <summary>Loading a settlement to change it, and telling someone else's save from an error.</summary>
internal static class PlatformSettlementConflicts
{
    public static async Task<PlatformSettlement> LoadAsync(IApplicationDbContext context, Guid settlementId, CancellationToken cancellationToken)
        => await context.PlatformSettlements
            .Include(s => s.Orders)
            .Include(s => s.Adjustments)
            .SingleOrDefaultAsync(s => s.Id == settlementId, cancellationToken)
           ?? throw new EntityNotFoundException(nameof(PlatformSettlement), settlementId);

    /// <summary>Saves a change made over the revision read; false, with the change undone, when someone saved meanwhile.</summary>
    public static async Task<bool> SaveAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await context.DiscardUnitOfWorkAsync(cancellationToken);
            return false;
        }
    }

    public static Task<bool> OpenedMeanwhileAsync(IApplicationDbContext context, OrderChannel channel, CancellationToken cancellationToken)
        => context.PlatformSettlements.AnyAsync(s => s.Channel == channel && s.Status == PlatformSettlementStatus.Open, cancellationToken);

    public static Task<bool> TakenMeanwhileAsync(
        IApplicationDbContext context, IReadOnlyList<Guid> orderIds, Guid? settlementId, CancellationToken cancellationToken)
    {
        var ids = orderIds.ToList();
        return context.PlatformSettlementOrders.AnyAsync(
            line => ids.Contains(line.OrderId) && line.IsActive && (settlementId == null || line.SettlementId != settlementId),
            cancellationToken);
    }
}

/// <summary>The activity entries of a settlement: when it is closed and when it is voided.</summary>
internal static class PlatformSettlementAudit
{
    public const string ClosedAction = "PlatformSettlementClosed";
    public const string VoidedAction = "PlatformSettlementVoided";

    public static AuditLog Closed(Guid tenantId, PlatformSettlement settlement, string performedBy)
        => Create(tenantId, settlement, ClosedAction, performedBy, new
        {
            settlement.Channel,
            settlement.PeriodFrom,
            settlement.PeriodTo,
            settlement.DepositedOn,
            settlement.DepositedAmount,
            settlement.Reference,
            settlement.OrdersTotal,
            settlement.AdjustmentsTotal,
            Orders = settlement.Orders.Count,
            Adjustments = settlement.Adjustments.Select(adjustment => new { adjustment.Kind, adjustment.Description, adjustment.Amount })
        });

    public static AuditLog Voided(Guid tenantId, PlatformSettlement settlement, PlatformSettlementStatus previousStatus, string performedBy)
        => Create(tenantId, settlement, VoidedAction, performedBy, new
        {
            settlement.Channel,
            settlement.PeriodFrom,
            settlement.PeriodTo,
            settlement.DepositedAmount,
            PreviousStatus = previousStatus,
            Orders = settlement.Orders.Count,
            Reason = settlement.VoidReason
        });

    private static AuditLog Create(Guid tenantId, PlatformSettlement settlement, string action, string performedBy, object metadata)
        => new(
            tenantId: tenantId,
            entityName: nameof(PlatformSettlement),
            entityId: settlement.Id.ToString(),
            action: action,
            performedBy: performedBy,
            metadata: JsonSerializer.Serialize(metadata));
}
