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

public record CreateTenantOrderCommand : IRequest<Result<OrderDto>>
{
    public Guid TenantId { get; init; }
    public string Phone { get; init; } = null!;
    public string? Name { get; init; }
    public string? CustomerNotes { get; init; }
    public string? TableNumber { get; init; }
    public string DefaultCountry { get; init; } = "MX";
    public bool CreatedByBusiness { get; init; } = true;
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public class CreateTenantOrderCommandValidator : AbstractValidator<CreateTenantOrderCommand>
{
    public CreateTenantOrderCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).MaximumLength(200);
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must have at least one item.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).GreaterThan(0);
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

    public CreateTenantOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper,
        ICustomerLookupService customerLookupService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _customerLookupService = customerLookupService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<OrderDto>> Handle(CreateTenantOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", request.TenantId);

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
            customerName: resolvedName,
            customerPhone: linkedCustomer.PhoneNormalized,
            customerNotes: request.CustomerNotes,
            tableNumber: request.TableNumber);

        foreach (var itemDto in request.Items)
        {
            var product = await _context.Products
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId && p.TenantId == request.TenantId, cancellationToken)
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
