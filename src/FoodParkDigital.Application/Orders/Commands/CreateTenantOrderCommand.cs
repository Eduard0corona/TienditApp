using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Catalog.Entities;
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

[RequiresAuthentication]
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
            .WithMessage($"Una orden puede tener como máximo {OrderLimits.MaxItemsPerOrder} lineas.");
        RuleFor(x => x.TableNumber).MaximumLength(50);
        RuleForEach(x => x.Items).SetValidator(new CreateOrderItemDtoValidator());
        RuleFor(x => x.ServiceDetail!.Pieces.Count)
            .LessThanOrEqualTo(OrderLimits.MaxServicePiecesPerOrder)
            .When(x => x.ServiceDetail is not null)
            .WithMessage($"Una orden puede tener como máximo {OrderLimits.MaxServicePiecesPerOrder} piezas.");

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
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOrderInventoryService _orderInventoryService;

    public CreateTenantOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ICustomerLookupService customerLookupService,
        IDateTimeProvider dateTimeProvider,
        IPhoneNormalizationService phoneNormalizationService,
        IOrderInventoryService? orderInventoryService = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _customerLookupService = customerLookupService;
        _dateTimeProvider = dateTimeProvider;
        _phoneNormalizationService = phoneNormalizationService;
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
                $"El negocio no tiene habilitado el módulo requerido para crear órdenes de {typeLabel}.");
        }

        // A mistyped number is the customer's to fix (400 in Spanish), not a server error.
        if (!_phoneNormalizationService.TryNormalizeToE164(request.Phone, request.DefaultCountry, out _))
            return Result.Failure<OrderDto>(PhoneNumberRules.InvalidPhoneMessage);

        var linkedCustomer = await _customerLookupService.ResolveForOrderAsync(
            request.TenantId,
            request.Phone,
            request.Name,
            request.DefaultCountry,
            cancellationToken);

        var salesSettings = await TenantSalesSettings.ReadAsync(_context, request.TenantId, cancellationToken);
        var tenantZone = TenantDateTimeResolver.Resolve(salesSettings.TimeZone);
        var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, tenantZone);

        var orderCountForTenant = await _context.Orders
            .IgnoreQueryFilters()
            .CountAsync(o => o.TenantId == request.TenantId, cancellationToken);
        var orderNumber = $"ORD-{orderCountForTenant + 1:D6}";

        // Orders placed by customers go to the default branch of the business named by the route.
        var defaultBranchId = await _context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.TenantId == request.TenantId && b.IsDefault)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(cancellationToken);

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
            tableNumber: request.TableNumber,
            pricesIncludeTax: salesSettings.PricesIncludeTax,
            branchId: defaultBranchId);
        var checklistCandidates = new List<(Product Product, CreateOrderItemDto Item)>();
        var lineBuilder = new OrderLineBuilder(localTimeOfDay, salesSettings.DefaultTaxRate);
        // The route names the tenant (validated by the controller), so products are narrowed to it explicitly.
        var products = await OrderCatalog.LoadProductsAsync(
            _context.Products.IgnoreQueryFilters().Where(p => p.TenantId == request.TenantId),
            request.Items,
            cancellationToken);

        foreach (var itemDto in request.Items)
        {
            var product = products[itemDto.ProductId];
            if (!request.CreatedByBusiness && !OrderProductClassifier.MatchesOrderType(request.OrderType, product.Unit))
            {
                var expectedLabel = request.OrderType == OrderType.Service ? "servicio" : "producto";
                return Result.Failure<OrderDto>(
                    $"El item '{product.Name}' no corresponde al tipo de orden '{expectedLabel}'.");
            }

            var built = lineBuilder.Build(product, itemDto);
            if (built.IsFailure)
                return Result.Failure<OrderDto>(built.Error!);

            order.AddItem(built.Value.Line);
            if (built.Value.IsTrackableService)
                checklistCandidates.Add((product, itemDto));
        }
        var quantitiesByProduct = lineBuilder.QuantitiesByProduct;

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
                var pieces = (int)item.Quantity;
                for (var index = 0; index < pieces; index++)
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

        // An order the customer placed (chat bot) is reviewed by the business; one the team captures follows its policy.
        var shortages = await _orderInventoryService.ApplyConsumptionForOrderAsync(
            order,
            cancellationToken,
            request.CreatedByBusiness ? StockShortageHandling.TenantPolicy : StockShortageHandling.Allow);

        order.MarkAsCreated();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<OrderDto>(order);
        dto.ApplyStateMetadata();
        dto.StockWarnings = shortages.Select(s => s.WarningMessage()).ToList();
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
            order.AdvanceOnCreation(defaultStatus, stateMachine, performedBy);
    }
}
