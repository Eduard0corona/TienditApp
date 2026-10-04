using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Commands;

[RequiresPermission(TenantPermission.ManageCustomers)]
public record SetCustomerActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetCustomerActiveCommandValidator : AbstractValidator<SetCustomerActiveCommand>
{
    public SetCustomerActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class SetCustomerActiveCommandHandler : IRequestHandler<SetCustomerActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetCustomerActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetCustomerActiveCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            customer.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageCatalog(tenantCapabilities))
            return Result.Failure("El tenant no tiene habilitado el modulo de clientes.");

        if (customer.IsPublicGeneral && !request.IsActive)
            return Result.Failure("No puedes desactivar el cliente publico en general.");

        customer.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
