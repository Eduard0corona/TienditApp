using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Branches;

/// <summary>Fields of a branch, as the branches screen sends them.</summary>
public abstract record BranchFields
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

    public BranchContact ToContact() => new(
        Description, AddressLine1, AddressLine2, City, State, Country, PostalCode, ContactName, ContactPhone);
}

internal static class BranchFieldRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : BranchFields
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(Branch.MaxNameLength);
        validator.RuleFor(x => x.Code).NotEmpty().MaximumLength(Branch.MaxCodeLength);
        validator.RuleFor(x => x.Description).MaximumLength(500);
        validator.RuleFor(x => x.AddressLine1).MaximumLength(250);
        validator.RuleFor(x => x.AddressLine2).MaximumLength(250);
        validator.RuleFor(x => x.City).MaximumLength(120);
        validator.RuleFor(x => x.State).MaximumLength(120);
        validator.RuleFor(x => x.Country).MaximumLength(120);
        validator.RuleFor(x => x.PostalCode).MaximumLength(20);
        validator.RuleFor(x => x.ContactName).MaximumLength(120);
        validator.RuleFor(x => x.ContactPhone).MaximumLength(30);
    }

    public static Task<bool> CodeTakenAsync(IApplicationDbContext context, string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var normalized = Branch.NormalizeCode(code);
        return context.Branches.AnyAsync(x => x.Code == normalized && x.Id != exceptId, cancellationToken);
    }

    /// <summary>
    /// Moves the default mark in two saves inside one transaction: the database allows one default branch per
    /// business, and a single save may write the new mark before clearing the old one.
    /// </summary>
    public static async Task MoveDefaultAsync(IApplicationDbContext context, Branch branch, CancellationToken cancellationToken)
    {
        var previous = await context.Branches
            .Where(x => x.IsDefault && x.Id != branch.Id)
            .ToListAsync(cancellationToken);
        previous.ForEach(x => x.UnmarkAsDefault());
        await context.SaveChangesAsync(cancellationToken);

        branch.MarkAsDefault();
        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Adds a branch. When the business uses inventory the branch gets its own stock location, so its sales
/// take stock from it instead of from the main warehouse.
/// </summary>
[RequiresTenantManager]
public record CreateBranchCommand : BranchFields, IRequest<Result<BranchDto>>
{
    public bool IsDefault { get; init; }
}

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator() => BranchFieldRules.Apply(this);
}

public class CreateBranchCommandHandler : IRequestHandler<CreateBranchCommand, Result<BranchDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateBranchCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public Task<Result<BranchDto>> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
        => _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var tenantId = _tenantService.TenantId;
            if (await _context.Branches.CountAsync(ct) >= Branch.MaxBranchesPerTenant)
                return Result.Failure<BranchDto>($"Un negocio tiene máximo {Branch.MaxBranchesPerTenant} sucursales.");
            if (await BranchFieldRules.CodeTakenAsync(_context, request.Code, null, ct))
                return Result.Failure<BranchDto>("Ya existe una sucursal con esa clave.");
            var planError = await SubscriptionRules.CheckBranchLimitAsync(_context, tenantId, ct);
            if (planError is not null)
                return Result.Failure<BranchDto>(planError);

            // The branch comes with its warehouse, which counts towards the business's locations.
            var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, ct);
            var withWarehouse = TenantCapabilityRules.CanAccessInventory(capabilities);
            if (withWarehouse && await _context.InventoryLocations.CountAsync(ct) >= InventoryLocation.MaxLocationsPerTenant)
                return Result.Failure<BranchDto>(InventoryLocationLimits.Reached);

            var makeDefault = request.IsDefault || !await _context.Branches.AnyAsync(x => x.IsDefault, ct);
            var branch = new Branch(tenantId, request.Name, request.Code, request.ToContact());
            _context.Branches.Add(branch);
            if (withWarehouse)
                _context.InventoryLocations.Add(await NewStockLocationAsync(branch, ct));

            await _context.SaveChangesAsync(ct);
            if (makeDefault)
                await BranchFieldRules.MoveDefaultAsync(_context, branch, ct);

            return Result.Success(BranchDto.FromEntity(branch));
        }, cancellationToken);

    private async Task<InventoryLocation> NewStockLocationAsync(Branch branch, CancellationToken cancellationToken)
    {
        // Location codes are unique per business too: a code already used by a location gets a suffix.
        var code = branch.Code;
        for (var suffix = 2; await _context.InventoryLocations.AnyAsync(x => x.Code == code, cancellationToken); suffix++)
        {
            var tail = $"-{suffix}";
            code = branch.Code[..Math.Min(branch.Code.Length, Branch.MaxCodeLength - tail.Length)] + tail;
        }

        return new InventoryLocation(
            tenantId: branch.TenantId,
            name: branch.Name,
            code: code,
            addressLine1: branch.AddressLine1,
            addressLine2: branch.AddressLine2,
            city: branch.City,
            state: branch.State,
            country: branch.Country,
            postalCode: branch.PostalCode,
            contactName: branch.ContactName,
            contactPhone: branch.ContactPhone,
            branchId: branch.Id);
    }
}

[RequiresTenantManager]
public record UpdateBranchCommand : BranchFields, IRequest<Result<BranchDto>>
{
    public Guid Id { get; init; }
}

public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        BranchFieldRules.Apply(this);
    }
}

public class UpdateBranchCommandHandler : IRequestHandler<UpdateBranchCommand, Result<BranchDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateBranchCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<BranchDto>> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (branch is null)
            return Result.Failure<BranchDto>(BranchRules.InvalidBranchMessage);
        if (await BranchFieldRules.CodeTakenAsync(_context, request.Code, branch.Id, cancellationToken))
            return Result.Failure<BranchDto>("Ya existe una sucursal con esa clave.");

        branch.Update(request.Name, request.Code, request.ToContact());
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(BranchDto.FromEntity(branch));
    }
}

/// <summary>Turns a branch on or off. The default branch and a branch with open drawers stay on.</summary>
[RequiresTenantManager]
public record SetBranchActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetBranchActiveCommandHandler : IRequestHandler<SetBranchActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetBranchActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetBranchActiveCommand request, CancellationToken cancellationToken)
    {
        var branch = await _context.Branches.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (branch is null)
            return Result.Failure(BranchRules.InvalidBranchMessage);

        if (request.IsActive)
        {
            if (!branch.IsActive)
            {
                var planError = await SubscriptionRules.CheckBranchLimitAsync(_context, branch.TenantId, cancellationToken);
                if (planError is not null)
                    return Result.Failure(planError);
            }

            branch.Activate();
        }
        else
        {
            if (branch.IsDefault)
                return Result.Failure("No puedes desactivar la sucursal principal. Elige otra como principal primero.");
            var hasOpenDrawers = await _context.CashSessions
                .AnyAsync(x => x.BranchId == branch.Id && x.Status == CashSessionStatus.Open, cancellationToken);
            if (hasOpenDrawers)
                return Result.Failure("Cierra las cajas abiertas de la sucursal antes de desactivarla.");
            branch.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Makes a branch the default one: sales and drawers that name no branch go to it.</summary>
[RequiresTenantManager]
public record SetDefaultBranchCommand : IRequest<Result>
{
    public Guid Id { get; init; }
}

public class SetDefaultBranchCommandHandler : IRequestHandler<SetDefaultBranchCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetDefaultBranchCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Result> Handle(SetDefaultBranchCommand request, CancellationToken cancellationToken)
        => _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var branch = await _context.Branches.FirstOrDefaultAsync(x => x.Id == request.Id, ct);
            if (branch is null)
                return Result.Failure(BranchRules.InvalidBranchMessage);
            if (!branch.IsActive)
                return Result.Failure("Activa la sucursal antes de hacerla la principal.");
            if (branch.IsDefault)
                return Result.Success();

            await BranchFieldRules.MoveDefaultAsync(_context, branch, ct);
            return Result.Success();
        }, cancellationToken);
}
