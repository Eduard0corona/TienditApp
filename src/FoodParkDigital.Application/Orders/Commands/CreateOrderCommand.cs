using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

public record CreateOrderCommand : IRequest<Result<OrderDto>>
{
    public string CustomerName { get; init; } = null!;
    public string? CustomerPhone { get; init; }
    public string? CustomerNotes { get; init; }
    public string? TableNumber { get; init; }
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
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(200);
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
    private readonly IMapper _mapper;

    public CreateOrderCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<OrderDto>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == _tenantService.TenantId, cancellationToken)
            ?? throw new EntityNotFoundException("Tenant", _tenantService.TenantId);

        // Generate order number
        var orderCount = await _context.Orders.CountAsync(cancellationToken);
        var orderNumber = $"ORD-{orderCount + 1:D6}";

        var order = new Order(
            tenantId: _tenantService.TenantId,
            orderNumber: orderNumber,
            businessType: tenant.BusinessType,
            customerName: request.CustomerName,
            customerPhone: request.CustomerPhone,
            customerNotes: request.CustomerNotes,
            tableNumber: request.TableNumber);

        // Add items
        foreach (var itemDto in request.Items)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == itemDto.ProductId, cancellationToken)
                ?? throw new EntityNotFoundException("Product", itemDto.ProductId);

            if (!product.IsAvailable)
                throw new DomainException($"Product '{product.Name}' is not available.");

            order.AddItem(product.Id, product.Name, itemDto.Quantity, product.PriceAmount, itemDto.Notes);
        }

        order.MarkAsCreated();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = _mapper.Map<OrderDto>(order);
        return Result.Success(dto);
    }
}
