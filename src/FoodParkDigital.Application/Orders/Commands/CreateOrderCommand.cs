using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

public record CreateOrderCommand : IRequest<Result<OrderDto>>
{
    public Guid? CustomerId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerNotes { get; init; }
    public string? TableNumber { get; init; }
    public bool CreatedByBusiness { get; init; } = true;
    public OrderType OrderType { get; init; } = OrderType.Retail;
    public ServiceOrderDetailInput? ServiceDetail { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public record CreateOrderItemDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public string? Notes { get; init; }
}

public record ServiceOrderDetailInput
{
    public DateTimeOffset? ReceivedAt { get; init; }
    public DateTimeOffset? PromisedAt { get; init; }
    public int Priority { get; init; } = 1;
    public string? IntakeNotes { get; init; }
    public string? DeliveryNotes { get; init; }
    public string? IntakeChecklistJson { get; init; }
    public string? BeforePhotosJson { get; init; }
    public string? AfterPhotosJson { get; init; }
    public List<ServiceOrderPieceInput> Pieces { get; init; } = new();
}

public record ServiceOrderPieceInput
{
    public string ItemName { get; init; } = null!;
    public Guid? ServiceProductId { get; init; }
    public bool IsTrackable { get; init; } = true;
    public bool NotifyCustomer { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Color { get; init; }
    public string? Size { get; init; }
    public string? Material { get; init; }
    public string? ConditionNotes { get; init; }
    public string? RequestedService { get; init; }
    public string? InternalNotes { get; init; }
}

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerName).MaximumLength(200);
        RuleFor(x => x.CustomerPhone).MaximumLength(50);
        RuleFor(x => x.OrderType).IsInEnum();
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });

        When(x => x.OrderType == OrderType.Service && !x.CreatedByBusiness, () =>
        {
            RuleFor(x => x.ServiceDetail).NotNull().WithMessage("El detalle del servicio es obligatorio.");
            RuleFor(x => x.ServiceDetail!.Priority).InclusiveBetween(1, 5);
            RuleFor(x => x.ServiceDetail!.IntakeNotes).MaximumLength(2000);
            RuleFor(x => x.ServiceDetail!.DeliveryNotes).MaximumLength(2000);
        });
    }
}

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly ICustomerLookupService _customerLookupService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderInventoryService _orderInventoryService;

    public CreateOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ICustomerLookupService customerLookupService,
        IDateTimeProvider dateTimeProvider,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _customerLookupService = customerLookupService;
        _dateTimeProvider = dateTimeProvider;
        _orderInventoryService = orderInventoryService ?? NoOpOrderInventoryService.Instance;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", _tenantService.TenantId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, request.OrderType))
        {
            var typeLabel = request.OrderType == OrderType.Service ? "servicios" : "ventas retail";
            return Result.Failure<OrderDto>(
                $"El tenant no tiene habilitado el modulo requerido para crear ordenes de {typeLabel}.");
        }

        string? customerName = request.CustomerName;
        string? customerPhone = request.CustomerPhone;

        if (request.CustomerId.HasValue)
        {
            var legacyCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, cancellationToken);

            if (legacyCustomer is null)
                return Result.Failure<OrderDto>("El cliente seleccionado no existe.");

            customerName ??= legacyCustomer.Name;
            customerPhone ??= legacyCustomer.Phone;
        }

        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return request.CustomerId.HasValue
                ? Result.Failure<OrderDto>("El cliente seleccionado no tiene telefono. Captura uno para continuar.")
                : Result.Failure<OrderDto>("Se requiere telefono del cliente para crear la orden.");
        }

        var linkedCustomer = await _customerLookupService.ResolveForOrderAsync(
            _tenantService.TenantId,
            customerPhone,
            customerName,
            "MX",
            cancellationToken);

        var tenantTimeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var tenantZone = TenantDateTimeResolver.Resolve(tenantTimeZoneId);
        var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, tenantZone);

        var orderCount = await _context.Orders.CountAsync(cancellationToken);
        var orderNumber = $"ORD-{orderCount + 1:D6}";

        var order = new Order(
            tenantId: _tenantService.TenantId,
            customerUserId: linkedCustomer.CustomerUserId,
            temporaryCustomerId: linkedCustomer.TemporaryCustomerId,
            createdByBusiness: request.CreatedByBusiness,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            orderType: request.OrderType,
            customerName: string.IsNullOrWhiteSpace(customerName) ? (linkedCustomer.ResolvedName ?? customerPhone) : customerName,
            customerPhone: linkedCustomer.PhoneNormalized,
            customerNotes: request.CustomerNotes,
            tableNumber: request.TableNumber);
        var checklistCandidates = new List<(Product Product, CreateOrderItemDto Item)>();

        foreach (var itemDto in request.Items)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId, cancellationToken)
                ?? throw new EntityNotFoundException("Product", itemDto.ProductId);

            if (!product.IsAvailableAt(localTimeOfDay))
                throw new DomainException($"Product '{product.Name}' is not available at this time.");

            if (!request.CreatedByBusiness && !OrderProductClassifier.MatchesOrderType(request.OrderType, product.Unit))
            {
                var expectedLabel = request.OrderType == OrderType.Service ? "servicio" : "producto";
                return Result.Failure<OrderDto>(
                    $"El item '{product.Name}' no corresponde al tipo de orden '{expectedLabel}'.");
            }

            order.AddItem(product.Id, product.Name, itemDto.Quantity, product.PriceAmount, itemDto.Notes);

            if (OrderProductClassifier.IsServiceUnit(product.Unit) && product.IsTrackable)
                checklistCandidates.Add((product, itemDto));
        }

        if (request.OrderType == OrderType.Service && request.ServiceDetail is not null)
        {
            var receivedAt = request.ServiceDetail.ReceivedAt ?? _dateTimeProvider.UtcNow;
            order.SetServiceDetail(
                receivedAt: receivedAt,
                promisedAt: request.ServiceDetail.PromisedAt,
                priority: request.ServiceDetail.Priority,
                intakeNotes: request.ServiceDetail.IntakeNotes?.Trim(),
                deliveryNotes: request.ServiceDetail.DeliveryNotes?.Trim(),
                intakeChecklistJson: request.ServiceDetail.IntakeChecklistJson,
                beforePhotosJson: request.ServiceDetail.BeforePhotosJson,
                afterPhotosJson: request.ServiceDetail.AfterPhotosJson);

        }

        var manualPieces = request.ServiceDetail?.Pieces
            .Where(piece => !string.IsNullOrWhiteSpace(piece.ItemName))
            .ToList()
            ?? [];

        if (manualPieces.Count > 0)
        {
            foreach (var piece in manualPieces)
            {
                order.AddServicePiece(
                    itemName: piece.ItemName.Trim(),
                    brand: piece.Brand?.Trim(),
                    model: piece.Model?.Trim(),
                    color: piece.Color?.Trim(),
                    size: piece.Size?.Trim(),
                    material: piece.Material?.Trim(),
                    conditionNotes: piece.ConditionNotes?.Trim(),
                    requestedService: piece.RequestedService?.Trim(),
                    internalNotes: piece.InternalNotes?.Trim(),
                    serviceProductId: piece.ServiceProductId,
                    isTrackable: piece.IsTrackable,
                    notifyCustomer: piece.NotifyCustomer);
            }
        }
        else
        {
            foreach (var (product, item) in checklistCandidates)
            {
                for (var index = 0; index < item.Quantity; index++)
                {
                    var itemName = item.Quantity > 1
                        ? $"{product.Name} #{index + 1}"
                        : product.Name;

                    order.AddServicePiece(
                        itemName: itemName,
                        brand: null,
                        model: null,
                        color: null,
                        size: null,
                        material: null,
                        conditionNotes: item.Notes?.Trim(),
                        requestedService: product.Name,
                        internalNotes: null,
                        serviceProductId: product.Id,
                        isTrackable: true,
                        notifyCustomer: product.NotifyCustomer);
                }
            }
        }

        if (request.CreatedByBusiness)
            ApplyBusinessDefaultStatus(order, _currentUserService.UserName);

        await _orderInventoryService.ApplyConsumptionForOrderAsync(order, cancellationToken);

        order.MarkAsCreated();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<OrderDto>(order);
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }

    private static void ApplyBusinessDefaultStatus(Order order, string? performedBy)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        var defaultStatus = order.OrderType switch
        {
            OrderType.Service => OrderStatus.Accepted,
            _ => order.BusinessType == BusinessType.Food ? OrderStatus.Confirmed : OrderStatus.Confirmed
        };

        if (stateMachine.GetAllowedTransitions(order.Status).Contains(defaultStatus))
            order.TransitionTo(defaultStatus, stateMachine, performedBy);
    }
}
