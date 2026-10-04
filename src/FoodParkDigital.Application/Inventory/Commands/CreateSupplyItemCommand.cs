using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record CreateSupplyItemCommand : IRequest<Result<SupplyItemDto>>
{
    public string Name { get; init; } = null!;
    public string? Code { get; init; }
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? CategoryName { get; init; }
    public UnitType Unit { get; init; } = UnitType.Unit;
    public decimal DefaultCost { get; init; }
    public decimal MinStock { get; init; }
    public decimal? MaxStock { get; init; }
    public decimal ReorderPoint { get; init; }
    public bool TrackBatches { get; init; }
    public bool TrackExpiration { get; init; }
    public bool IsPerishable { get; init; }
    public bool IsCritical { get; init; }
    public decimal? TaxRate { get; init; }
    public string? Notes { get; init; }
}

public class CreateSupplyItemCommandValidator : AbstractValidator<CreateSupplyItemCommand>
{
    public CreateSupplyItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).MaximumLength(100);
        RuleFor(x => x.Sku).MaximumLength(100);
        RuleFor(x => x.Barcode).MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Brand).MaximumLength(120);
        RuleFor(x => x.Model).MaximumLength(120);
        RuleFor(x => x.CategoryName).MaximumLength(120);
        RuleFor(x => x.Unit).IsInEnum();
        RuleFor(x => x.DefaultCost).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinStock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxStock).GreaterThanOrEqualTo(0).When(x => x.MaxStock.HasValue);
        RuleFor(x => x.ReorderPoint).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TaxRate).InclusiveBetween(0, 100).When(x => x.TaxRate.HasValue);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x)
            .Must(x => !x.MaxStock.HasValue || x.MaxStock.Value >= x.MinStock)
            .WithMessage("El stock maximo no puede ser menor al stock minimo.");
    }
}

public class CreateSupplyItemCommandHandler : IRequestHandler<CreateSupplyItemCommand, Result<SupplyItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateSupplyItemCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<SupplyItemDto>> Handle(CreateSupplyItemCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<SupplyItemDto>("El tenant no tiene habilitado el modulo de inventario.");

        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
        var sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();
        var barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();

        if (code is not null && await _context.SupplyItems.AnyAsync(x => x.Code == code, cancellationToken))
            return Result.Failure<SupplyItemDto>("Ya existe un insumo con ese codigo.");

        if (sku is not null && await _context.SupplyItems.AnyAsync(x => x.Sku == sku, cancellationToken))
            return Result.Failure<SupplyItemDto>("Ya existe un insumo con ese SKU.");

        if (barcode is not null && await _context.SupplyItems.AnyAsync(x => x.Barcode == barcode, cancellationToken))
            return Result.Failure<SupplyItemDto>("Ya existe un insumo con ese codigo de barras.");

        var entity = new SupplyItem(
            tenantId: _tenantService.TenantId,
            name: request.Name.Trim(),
            unit: request.Unit,
            code: code,
            sku: sku,
            barcode: barcode,
            description: request.Description?.Trim(),
            brand: request.Brand?.Trim(),
            model: request.Model?.Trim(),
            categoryName: request.CategoryName?.Trim(),
            defaultCost: request.DefaultCost,
            minStock: request.MinStock,
            maxStock: request.MaxStock,
            reorderPoint: request.ReorderPoint,
            trackBatches: request.TrackBatches,
            trackExpiration: request.TrackExpiration,
            isPerishable: request.IsPerishable,
            isCritical: request.IsCritical,
            taxRate: request.TaxRate,
            notes: request.Notes?.Trim());

        _context.SupplyItems.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<SupplyItemDto>(entity));
    }
}
