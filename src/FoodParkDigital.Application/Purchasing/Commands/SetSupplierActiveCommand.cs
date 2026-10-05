using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record SetSupplierActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetSupplierActiveCommandValidator : AbstractValidator<SetSupplierActiveCommand>
{
    public SetSupplierActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class SetSupplierActiveCommandHandler : IRequestHandler<SetSupplierActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetSupplierActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetSupplierActiveCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.Suppliers
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Supplier", request.Id);

        entity.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
