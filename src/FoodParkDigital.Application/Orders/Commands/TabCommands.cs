using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>Adds a round to an open tab and sends it to the kitchen.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Food)]
public record AddTabRoundCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public class AddTabRoundCommandValidator : AbstractValidator<AddTabRoundCommand>
{
    public AddTabRoundCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Items).NotEmpty().WithMessage("La ronda necesita al menos un producto.");
        RuleFor(x => x.Items.Count)
            .LessThanOrEqualTo(OrderLimits.MaxItemsPerOrder)
            .WithMessage($"Una orden puede tener como máximo {OrderLimits.MaxItemsPerOrder} lineas.");
        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemDtoValidator());
    }
}

public class AddTabRoundCommandHandler : IRequestHandler<AddTabRoundCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMapper _mapper;

    public AddTabRoundCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _mapper = mapper;
    }

    public async Task<Result<OrderDto>> Handle(AddTabRoundCommand request, CancellationToken cancellationToken)
    {
        var order = await TabLoader.LoadAsync(_context, request.OrderId, cancellationToken);
        if (!order.IsOpenTab)
            return Result.Failure<OrderDto>("La cuenta ya está cerrada: abre una nueva.");
        if (order.Items.Count + request.Items.Count > OrderLimits.MaxItemsPerOrder)
            return Result.Failure<OrderDto>($"Una cuenta puede tener como máximo {OrderLimits.MaxItemsPerOrder} lineas.");

        var salesSettings = await TenantSalesSettings.ReadAsync(_context, _tenantService.TenantId, cancellationToken);
        var tenantZone = TenantDateTimeResolver.Resolve(salesSettings.TimeZone);
        var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, tenantZone);
        var lineBuilder = new OrderLineBuilder(localTimeOfDay, salesSettings.DefaultTaxRate, order.Items);
        var products = await OrderCatalog.LoadProductsAsync(_context.Products, request.Items, cancellationToken);

        var lines = new List<OrderLineRequest>();
        foreach (var itemDto in request.Items)
        {
            var product = products[itemDto.ProductId];
            if (OrderProductClassifier.IsServiceUnit(product.Unit))
                return Result.Failure<OrderDto>($"'{product.Name}' es un servicio: no se agrega a una cuenta de consumo.");

            var built = lineBuilder.Build(product, itemDto);
            if (built.IsFailure)
                return Result.Failure<OrderDto>(built.Error!);
            lines.Add(built.Value.Line);
        }

        order.AddRound(lines, _dateTimeProvider.UtcNow, _currentUserService.UserName);
        return await TabLoader.SaveAsync(_context, _mapper, order, cancellationToken);
    }
}

/// <summary>Closes a paid tab, frees its table and consumes the inventory of all its rounds.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Food)]
public record CloseTabCommand(Guid OrderId) : IRequest<Result<OrderDto>>;

public class CloseTabCommandHandler : IRequestHandler<CloseTabCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly IOrderInventoryService _orderInventoryService;

    public CloseTabCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper,
        IOrderInventoryService orderInventoryService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _orderInventoryService = orderInventoryService;
    }

    public async Task<Result<OrderDto>> Handle(CloseTabCommand request, CancellationToken cancellationToken)
    {
        var order = await TabLoader.LoadAsync(_context, request.OrderId, cancellationToken);
        if (!order.IsOpenTab)
            return Result.Failure<OrderDto>("La cuenta ya está cerrada.");
        if (order.Balance > 0)
        {
            return Result.Failure<OrderDto>(
                $"La cuenta tiene un saldo de {OrderStatusLabels.FormatMoney(order.Balance)}: cóbralo antes de cerrarla.");
        }

        order.CloseTab(OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), _currentUserService.UserName);
        // The rounds were served and paid already: missing supplies never keep the table's tab open.
        await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken, StockShortageHandling.Allow);
        return await TabLoader.SaveAsync(_context, _mapper, order, cancellationToken);
    }
}

/// <summary>Moves an open tab to another free table.</summary>
[RequiresPermission(TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Food)]
public record MoveTabCommand(Guid OrderId, Guid DiningTableId) : IRequest<Result<OrderDto>>;

public class MoveTabCommandHandler : IRequestHandler<MoveTabCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public MoveTabCommandHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<Result<OrderDto>> Handle(MoveTabCommand request, CancellationToken cancellationToken)
    {
        var order = await TabLoader.LoadAsync(_context, request.OrderId, cancellationToken);
        if (!order.IsOpenTab)
            return Result.Failure<OrderDto>("La cuenta ya está cerrada.");

        var table = await _context.DiningTables.FirstOrDefaultAsync(t => t.Id == request.DiningTableId, cancellationToken);
        if (table is null || !table.IsActive)
            return Result.Failure<OrderDto>("La mesa no existe o está desactivada.");
        if (order.OpenTableId == table.Id)
            return Result.Failure<OrderDto>("La cuenta ya está en esa mesa.");
        if (await _context.Orders.AnyAsync(o => o.OpenTableId == table.Id, cancellationToken))
            return Result.Failure<OrderDto>($"La mesa {table.Name} ya tiene una cuenta abierta.");

        order.MoveToTable(table.Id, table.Name);
        return await TabLoader.SaveAsync(_context, _mapper, order, cancellationToken);
    }
}

/// <summary>Moves a round of an open tab forward: preparing, ready or served.</summary>
[RequiresPermission(TenantPermission.ManageServiceBoard, TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Food)]
public record UpdateKitchenTicketStatusCommand(Guid TicketId, KitchenTicketStatus Status) : IRequest<Result>;

public class UpdateKitchenTicketStatusCommandValidator : AbstractValidator<UpdateKitchenTicketStatusCommand>
{
    public UpdateKitchenTicketStatusCommandValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum().NotEqual(KitchenTicketStatus.Pending);
    }
}

public class UpdateKitchenTicketStatusCommandHandler : IRequestHandler<UpdateKitchenTicketStatusCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateKitchenTicketStatusCommandHandler(IApplicationDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(UpdateKitchenTicketStatusCommand request, CancellationToken cancellationToken)
    {
        var orderId = await _context.KitchenTickets
            .Where(t => t.Id == request.TicketId)
            .Select(t => (Guid?)t.OrderId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("KitchenTicket", request.TicketId);

        var order = await _context.Orders
            .Include(o => o.KitchenTickets)
            .FirstAsync(o => o.Id == orderId, cancellationToken);
        if (!order.IsOpenTab)
            return Result.Failure("La cuenta ya está cerrada.");

        order.ChangeKitchenTicketStatus(request.TicketId, request.Status, _dateTimeProvider.UtcNow);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure("Otro dispositivo cambió la cuenta al mismo tiempo; actualiza e intenta de nuevo.");
        }

        return Result.Success();
    }
}

internal static class TabLoader
{
    public static async Task<Order> LoadAsync(IApplicationDbContext context, Guid orderId, CancellationToken cancellationToken)
        => await context.Orders
            .Include(o => o.Items).ThenInclude(i => i.Options)
            .Include(o => o.KitchenTickets)
            .Include(o => o.Payments)
            .Include(o => o.Refunds).ThenInclude(r => r.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", orderId);

    public static async Task<Result<OrderDto>> SaveAsync(
        IApplicationDbContext context,
        IMapper mapper,
        Order order,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<OrderDto>("Otro dispositivo cambió la cuenta al mismo tiempo; actualiza e intenta de nuevo.");
        }
        catch (DbUpdateException) when (order.OpenTableId.HasValue)
        {
            return Result.Failure<OrderDto>("Esa mesa ya tiene una cuenta abierta.");
        }

        var dto = mapper.Map<OrderDto>(order);
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }
}
