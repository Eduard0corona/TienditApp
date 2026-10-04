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

public record CreateTenantOrderCommand : IRequest<Result<OrderDto>>
{
    public Guid TenantId { get; init; }
    public string Phone { get; init; } = null!;
    public string? Name { get; init; }
    public string? CustomerNotes { get; init; }
    public string? TableNumber { get; init; }
    public string DefaultCountry { get; init; } = "MX";
    public bool CreatedByBusiness { get; init; } = true;
    public OrderType OrderType { get; init; } = OrderType.Retail;
    public ServiceOrderDetailInput? ServiceDetail { get; init; }
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public class CreateTenantOrderCommandValidator : AbstractValidator<CreateTenantOrderCommand>
{
    public CreateTenantOrderCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.OrderType).IsInEnum();
        RuleFor(x => x.Items).NotEmpty().WithMessage("La orden debe tener al menos un artículo.");
        RuleFor(x => x.Items.Count)
            .LessThanOrEqualTo(OrderLimits.MaxItemsPerOrder)
            .WithMessage($"Una orden puede tener como maximo {OrderLimits.MaxItemsPerOrder} lineas.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0).LessThanOrEqualTo(OrderLimits.MaxQuantityPerLine);
        });
        RuleFor(x => x.ServiceDetail!.Pieces.Count)
            .LessThanOrEqualTo(OrderLimits.MaxServicePiecesPerOrder)
            .When(x => x.ServiceDetail is not null)
            .WithMessage($"Una orden puede tener como maximo {OrderLimits.MaxServicePiecesPerOrder} piezas.");

        When(x => x.OrderType == OrderType.Service && !x.CreatedByBusiness, () =>
        {
            RuleFor(x => x.ServiceDetail).NotNull().WithMessage("El detalle del servicio es obligatorio.");
            RuleFor(x => x.ServiceDetail!.Priority).InclusiveBetween(1, 5);
        });
    }
}

public class CreateTenantOrderCommandHandler : IRequestHandler<CreateTenantOrderCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly ICustomerLookupService _customerLookupService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderInventoryService _orderInventoryService;

    public CreateTenantOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ICustomerLookupService customerLookupService,
        IDateTimeProvider dateTimeProvider,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _customerLookupService = customerLookupService;
        _dateTimeProvider = dateTimeProvider;
        _orderInventoryService = orderInventoryService ?? NoOpOrderInventoryService.Instance;
    }

    public async Task<Result<OrderDto>> Handle(CreateTenantOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", request.TenantId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            request.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, request.OrderType))
        {
            var typeLabel = request.OrderType == OrderType.Service ? "servicios" : "ventas retail";
            return Result.Failure<OrderDto>(
                $"El tenant no tiene habilitado el modulo requerido para crear ordenes de {typeLabel}.");
        }

        var linkedCustomer = await _customerLookupService.ResolveForOrderAsync(
            request.TenantId,
            request.Phone,
            request.Name,
            request.DefaultCountry,
            cancellationToken);

        var tenantTimeZoneId = await _context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == request.TenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var tenantZone = TenantDateTimeResolver.Resolve(tenantTimeZoneId);
        var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, tenantZone);

        var orderCountForTenant = await _context.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.TenantId == request.TenantId, cancellationToken);
        var orderNumber = $"ORD-{orderCountForTenant + 1:D6}";

        var resolvedName = string.IsNullOrWhiteSpace(linkedCustomer.ResolvedName)
            ? request.Phone
            : linkedCustomer.ResolvedName!.Trim();

        var order = new Order(
            tenantId: request.TenantId,
            customerUserId: linkedCustomer.CustomerUserId,
            temporaryCustomerId: linkedCustomer.TemporaryCustomerId,
            createdByBusiness: request.CreatedByBusiness,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            orderType: request.OrderType,
            customerName: resolvedName,
            customerPhone: linkedCustomer.PhoneNormalized,
            customerNotes: request.CustomerNotes,
            tableNumber: request.TableNumber);
        var checklistCandidates = new List<(Product Product, CreateOrderItemDto Item)>();
        var quantitiesByProduct = new Dictionary<Guid, int>();

        foreach (var itemDto in request.Items)
        {
            var product = await _context.Products
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId && p.TenantId == request.TenantId, cancellationToken)
                ?? throw new EntityNotFoundException("Product", itemDto.ProductId);

            if (!product.IsAvailableAt(localTimeOfDay))
                throw new DomainException($"El producto '{product.Name}' no está disponible en este horario.");

            if (!request.CreatedByBusiness && !OrderProductClassifier.MatchesOrderType(request.OrderType, product.Unit))
            {
                var expectedLabel = request.OrderType == OrderType.Service ? "servicio" : "producto";
                return Result.Failure<OrderDto>(
                    $"El item '{product.Name}' no corresponde al tipo de orden '{expectedLabel}'.");
            }

            var quantityError = OrderLimits.TrackProductQuantity(quantitiesByProduct, product, itemDto.Quantity);
            if (quantityError is not null)
                return Result.Failure<OrderDto>(quantityError);

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

        var piecesError = OrderLimits.ValidateServicePieces(
            manualPieces,
            checklistCandidates,
            quantitiesByProduct.Keys.ToHashSet());
        if (piecesError is not null)
            return Result.Failure<OrderDto>(piecesError);

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
            _ => OrderStatus.Confirmed
        };

        if (stateMachine.GetAllowedTransitions(order.Status).Contains(defaultStatus))
            order.TransitionTo(defaultStatus, stateMachine, performedBy);
    }
}
