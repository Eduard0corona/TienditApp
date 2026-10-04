using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record CreateInventoryLocationCommand : IRequest<Result<InventoryLocationDto>>
{
    public string Name { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public string? PostalCode { get; init; }
    public string? ContactName { get; init; }
    public string? ContactPhone { get; init; }
    public bool IsDefault { get; init; }
}

public class CreateInventoryLocationCommandValidator : AbstractValidator<CreateInventoryLocationCommand>
{
    public CreateInventoryLocationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.AddressLine1).MaximumLength(250);
        RuleFor(x => x.AddressLine2).MaximumLength(250);
        RuleFor(x => x.City).MaximumLength(120);
        RuleFor(x => x.State).MaximumLength(120);
        RuleFor(x => x.Country).MaximumLength(120);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.ContactName).MaximumLength(120);
        RuleFor(x => x.ContactPhone).MaximumLength(30);
    }
}

public class CreateInventoryLocationCommandHandler : IRequestHandler<CreateInventoryLocationCommand, Result<InventoryLocationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateInventoryLocationCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<InventoryLocationDto>> Handle(CreateInventoryLocationCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<InventoryLocationDto>("El tenant no tiene habilitado el modulo de inventario.");

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (await _context.InventoryLocations.AnyAsync(x => x.Code == code, cancellationToken))
            return Result.Failure<InventoryLocationDto>("Ya existe una ubicacion con ese codigo.");

        var hasAnyLocation = await _context.InventoryLocations.AnyAsync(cancellationToken);
        var makeDefault = request.IsDefault || !hasAnyLocation;
        if (makeDefault)
        {
            var previousDefault = await _context.InventoryLocations
                .Where(x => x.IsDefault)
                .ToListAsync(cancellationToken);
            previousDefault.ForEach(x => x.SetDefault(false));
        }

        var entity = new InventoryLocation(
            tenantId: _tenantService.TenantId,
            name: name,
            code: code,
            description: request.Description?.Trim(),
            addressLine1: request.AddressLine1?.Trim(),
            addressLine2: request.AddressLine2?.Trim(),
            city: request.City?.Trim(),
            state: request.State?.Trim(),
            country: request.Country?.Trim(),
            postalCode: request.PostalCode?.Trim(),
            contactName: request.ContactName?.Trim(),
            contactPhone: request.ContactPhone?.Trim(),
            isDefault: makeDefault);

        _context.InventoryLocations.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<InventoryLocationDto>(entity));
    }
}
