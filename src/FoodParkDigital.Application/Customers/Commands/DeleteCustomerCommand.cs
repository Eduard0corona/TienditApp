using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

public record DeleteCustomerCommand(Guid Id) : IRequest<Result>;

public class DeleteCustomerCommandValidator : AbstractValidator<DeleteCustomerCommand>
{
    public DeleteCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteCustomerCommandHandler : IRequestHandler<DeleteCustomerCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteCustomerCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.Id);

        if (customer.IsPublicGeneral)
            return Result.Failure("No puedes eliminar el cliente público en general.");

        var inUse = await _context.Orders
            .AnyAsync(
                o => o.CustomerName == customer.Name
                    && o.CustomerPhone == customer.Phone,
                cancellationToken);
        if (inUse)
            return Result.Failure("No puedes eliminar un cliente con órdenes relacionadas.");

        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
