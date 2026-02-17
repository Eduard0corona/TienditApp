using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

public record AddOrderPaymentCommand : IRequest<Result<OrderDto>>
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public OrderPaymentMethod Method { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}

public class AddOrderPaymentCommandValidator : AbstractValidator<AddOrderPaymentCommand>
{
    public AddOrderPaymentCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public class AddOrderPaymentCommandHandler : IRequestHandler<AddOrderPaymentCommand, Result<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMapper _mapper;

    public AddOrderPaymentCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _mapper = mapper;
    }

    public async Task<Result<OrderDto>> Handle(AddOrderPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        if (order.Status == OrderStatus.Cancelled)
            return Result.Failure<OrderDto>("No se pueden registrar pagos en una orden cancelada.");

        order.RegisterPayment(
            amount: request.Amount,
            method: request.Method,
            paidAt: request.PaidAt ?? _dateTimeProvider.UtcNow,
            reference: request.Reference?.Trim(),
            notes: request.Notes?.Trim());

        await _context.SaveChangesAsync(cancellationToken);

        var updatedOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);

        var dto = _mapper.Map<OrderDto>(updatedOrder);
        dto.ApplyStateMetadata();
        return Result.Success(dto);
    }
}
