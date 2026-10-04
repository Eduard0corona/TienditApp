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
    private readonly IPhoneNormalizationService _phoneNormalizationService;

    public DeleteCustomerCommandHandler(
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService)
    {
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
    }

    public async Task<Result> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            customer.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageCatalog(tenantCapabilities))
            return Result.Failure("El negocio no tiene habilitado el módulo de clientes.");

        if (customer.IsPublicGeneral)
            return Result.Failure("No puedes eliminar el cliente público en general.");

        // Orders store the phone normalized to E.164 while customers keep it as typed; comparing the raw
        // values (and the name) almost never matched, so customers with orders were hard-deleted.
        var phoneCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(customer.Phone))
        {
            phoneCandidates.Add(customer.Phone.Trim());
            try
            {
                phoneCandidates.Add(_phoneNormalizationService.NormalizeToE164(customer.Phone, "MX"));
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or DomainException)
            {
                // Not a valid phone number: only the raw value can match.
            }
        }

        var inUse = phoneCandidates.Count > 0
            && await _context.Orders.AnyAsync(
                o => o.CustomerPhone != null && phoneCandidates.Contains(o.CustomerPhone),
                cancellationToken);
        if (inUse)
            return Result.Failure("No puedes eliminar un cliente con órdenes relacionadas.");

        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
