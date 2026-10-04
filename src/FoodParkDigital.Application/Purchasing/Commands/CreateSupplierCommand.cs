using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Purchasing.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
public record CreateSupplierCommand : IRequest<Result<SupplierDto>>
{
    public string Name { get; init; } = null!;
    public string? ContactName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? TaxId { get; init; }
    public string? Address { get; init; }
    public string? Notes { get; init; }
}

public class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactName).MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(200);
        RuleFor(x => x.TaxId).MaximumLength(80);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, Result<SupplierDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateSupplierCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<SupplierDto>> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<SupplierDto>("El negocio no tiene habilitado el módulo de inventario/compras.");

        var entity = new Supplier(
            tenantId: _tenantService.TenantId,
            name: request.Name,
            contactName: request.ContactName,
            phone: request.Phone,
            email: request.Email,
            taxId: request.TaxId,
            address: request.Address,
            notes: request.Notes);

        _context.Suppliers.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<SupplierDto>(entity));
    }
}
