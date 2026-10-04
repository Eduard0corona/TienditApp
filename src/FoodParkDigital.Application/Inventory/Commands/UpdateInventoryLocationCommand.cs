using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record UpdateInventoryLocationCommand : IRequest<Result<InventoryLocationDto>>
{
    public Guid Id { get; init; }
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
    /// <summary>Moves the stock location to this branch. Left out, the location keeps its branch.</summary>
    public Guid? BranchId { get; init; }
    /// <summary>Makes it a warehouse that serves every branch.</summary>
    public bool DetachFromBranch { get; init; }
}

public class UpdateInventoryLocationCommandValidator : AbstractValidator<UpdateInventoryLocationCommand>
{
    public UpdateInventoryLocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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

public class UpdateInventoryLocationCommandHandler : IRequestHandler<UpdateInventoryLocationCommand, Result<InventoryLocationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public UpdateInventoryLocationCommandHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<Result<InventoryLocationDto>> Handle(UpdateInventoryLocationCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.InventoryLocations
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("InventoryLocation", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            entity.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<InventoryLocationDto>("El negocio no tiene habilitado el módulo de inventario.");

        var branchError = await InventoryLocationBranches.ValidateAsync(_context, entity.TenantId, request.BranchId, cancellationToken);
        if (branchError is not null)
            return Result.Failure<InventoryLocationDto>(branchError);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _context.InventoryLocations.AnyAsync(x => x.Id != request.Id && x.Code == code, cancellationToken))
            return Result.Failure<InventoryLocationDto>("Ya existe una ubicación con ese código.");

        entity.Update(
            name: request.Name.Trim(),
            code: code,
            description: request.Description?.Trim(),
            addressLine1: request.AddressLine1?.Trim(),
            addressLine2: request.AddressLine2?.Trim(),
            city: request.City?.Trim(),
            state: request.State?.Trim(),
            country: request.Country?.Trim(),
            postalCode: request.PostalCode?.Trim(),
            contactName: request.ContactName?.Trim(),
            contactPhone: request.ContactPhone?.Trim());
        if (request.BranchId is not null)
            entity.AssignToBranch(request.BranchId);
        else if (request.DetachFromBranch)
            entity.AssignToBranch(null);

        if (request.IsDefault)
        {
            var previousDefault = await _context.InventoryLocations
                .Where(x => x.IsDefault && x.Id != request.Id)
                .ToListAsync(cancellationToken);
            previousDefault.ForEach(x => x.SetDefault(false));
            entity.SetDefault(true);
        }
        else
        {
            var hasAnotherDefault = await _context.InventoryLocations
                .AnyAsync(x => x.Id != request.Id && x.IsDefault, cancellationToken);

            if (!hasAnotherDefault)
                entity.SetDefault(true);
            else
                entity.SetDefault(false);
        }

        await _context.SaveChangesAsync(cancellationToken);
        var dto = await InventoryLocationBranches.NameAsync(_context, [_mapper.Map<InventoryLocationDto>(entity)], cancellationToken);
        return Result.Success(dto[0]);
    }
}
