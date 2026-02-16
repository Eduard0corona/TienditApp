using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.Orders.DTOs;
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
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public record CreateOrderItemDto
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public string? Notes { get; init; }
}

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerName).MaximumLength(200);
        RuleFor(x => x.CustomerPhone).MaximumLength(50);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
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

    public CreateOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ICustomerLookupService customerLookupService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _customerLookupService = customerLookupService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", _tenantService.TenantId);

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
            customerName: string.IsNullOrWhiteSpace(customerName) ? (linkedCustomer.ResolvedName ?? customerPhone) : customerName,
            customerPhone: linkedCustomer.PhoneNormalized,
            customerNotes: request.CustomerNotes,
            tableNumber: request.TableNumber);

        foreach (var itemDto in request.Items)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId, cancellationToken)
                ?? throw new EntityNotFoundException("Product", itemDto.ProductId);

            if (!product.IsAvailableAt(localTimeOfDay))
                throw new DomainException($"Product '{product.Name}' is not available at this time.");

            order.AddItem(product.Id, product.Name, itemDto.Quantity, product.PriceAmount, itemDto.Notes);
        }

        if (request.CreatedByBusiness)
            ApplyBusinessDefaultStatus(order, _currentUserService.UserName);

        order.MarkAsCreated();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<OrderDto>(order);
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }

    private static void ApplyBusinessDefaultStatus(Order order, string? performedBy)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType);
        var defaultStatus = order.BusinessType switch
        {
            BusinessType.Food => OrderStatus.Confirmed,
            BusinessType.Retail => OrderStatus.Confirmed,
            BusinessType.Service => OrderStatus.Accepted,
            _ => OrderStatus.Pending
        };

        if (stateMachine.GetAllowedTransitions(order.Status).Contains(defaultStatus))
            order.TransitionTo(defaultStatus, stateMachine, performedBy);
    }
}
