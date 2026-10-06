using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

[RequiresPermission(TenantPermission.ManageCatalog)]
public record UpdateProductCommand : IRequest<Result<ProductDto>>
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public decimal PriceAmount { get; init; }
    public string PriceCurrency { get; init; } = "MXN";
    public int? PreparationTimeMinutes { get; init; }
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public int SortOrder { get; init; }
    public bool IsFeatured { get; init; }
    public string? Tags { get; init; }
    public decimal? CompareAtPrice { get; init; }
    /// <summary>IVA rate in percent (16, 8 or 0); null uses the business default rate.</summary>
    public decimal? TaxRate { get; init; }
    /// <summary>Exempt from IVA; an exempt product has no rate.</summary>
    public bool IsTaxExempt { get; init; }
    /// <summary>CFDI ClaveProdServ (8 digits); empty uses the generic code 01010101.</summary>
    public string? SatProductCode { get; init; }
    /// <summary>CFDI ClaveUnidad (H87, KGM...); empty uses the code of the unit.</summary>
    public string? SatUnitCode { get; init; }
    /// <summary>CFDI ObjetoImp: subject to tax (02) unless the product is outside IVA (01).</summary>
    public CfdiTaxObject TaxObject { get; init; } = CfdiTaxObject.Subject;
    public int? Calories { get; init; }
    public string? Allergens { get; init; }
    public int? MaxQuantityPerOrder { get; init; }
    public UnitType? Unit { get; init; }
    public TimeSpan? AvailableFromTime { get; init; }
    public TimeSpan? AvailableToTime { get; init; }
    public bool IsTrackable { get; init; }
    public bool NotifyCustomer { get; init; }
}

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.PriceAmount).GreaterThan(0);
        RuleFor(x => x.PriceCurrency).NotEmpty().MaximumLength(3);
        RuleFor(x => x.Sku).MaximumLength(100);
        RuleFor(x => x.Barcode).MaximumLength(50);
        RuleFor(x => x.Tags).MaximumLength(500);
        RuleFor(x => x.Unit).IsInEnum().When(x => x.Unit.HasValue);
        RuleFor(x => x.Allergens).MaximumLength(500);
        RuleFor(x => x.CompareAtPrice)
            .GreaterThan(0).When(x => x.CompareAtPrice.HasValue);
        RuleFor(x => x)
            .Must(x => !x.CompareAtPrice.HasValue || x.CompareAtPrice.Value > x.PriceAmount)
            .WithMessage("El precio anterior debe ser mayor al precio actual.");
        RuleFor(x => x.TaxRate!.Value)
            .Must(VatRateRules.IsAllowed)
            .When(x => x.TaxRate.HasValue)
            .WithMessage(VatRateRules.InvalidRateMessage);
        RuleFor(x => x.TaxRate)
            .Null()
            .When(x => x.IsTaxExempt)
            .WithMessage("Un producto exento de IVA no lleva tasa.");
        RuleFor(x => x.SatProductCode)
            .Must(code => code!.Trim().Length == 8 && code.Trim().All(char.IsAsciiDigit))
            .When(x => !string.IsNullOrWhiteSpace(x.SatProductCode))
            .WithMessage("La clave de producto del SAT tiene 8 dígitos (por ejemplo 50202301).");
        RuleFor(x => x.SatUnitCode)
            .Must(code => code!.Trim().Length is 2 or 3 && code.Trim().All(char.IsAsciiLetterOrDigit))
            .When(x => !string.IsNullOrWhiteSpace(x.SatUnitCode))
            .WithMessage("La clave de unidad del SAT tiene 2 o 3 letras o números (por ejemplo H87 o KGM).");
        RuleFor(x => x.TaxObject).IsInEnum();
        RuleFor(x => x)
            .Must(x => x.TaxObject != CfdiTaxObject.NotSubject || x.IsTaxExempt || x.TaxRate == 0)
            .WithMessage("Un producto que no es objeto de impuesto debe ser exento o con tasa 0 %.");
        RuleFor(x => x.Calories)
            .GreaterThanOrEqualTo(0).When(x => x.Calories.HasValue);
        RuleFor(x => x.MaxQuantityPerOrder)
            .GreaterThan(0).When(x => x.MaxQuantityPerOrder.HasValue);
        RuleFor(x => x.PreparationTimeMinutes)
            .GreaterThan(0).When(x => x.PreparationTimeMinutes.HasValue);
        RuleFor(x => x)
            .Must(x => x.AvailableFromTime.HasValue == x.AvailableToTime.HasValue)
            .WithMessage("Debes especificar hora de inicio y fin para disponibilidad por horario.");
        RuleFor(x => x)
            .Must(x => !x.AvailableFromTime.HasValue || x.AvailableFromTime.Value != x.AvailableToTime!.Value)
            .WithMessage("La hora de inicio y fin no pueden ser iguales.");
        RuleFor(x => x.NotifyCustomer)
            .Equal(false)
            .When(x => !x.IsTrackable)
            .WithMessage("No puedes notificar al cliente en un servicio no rastreable.");

        When(x => IsServiceUnit(x.Unit), () =>
        {
            RuleFor(x => x.Sku)
                .Must(string.IsNullOrWhiteSpace)
                .WithMessage("Los servicios no admiten SKU.");
            RuleFor(x => x.Barcode)
                .Must(string.IsNullOrWhiteSpace)
                .WithMessage("Los servicios no admiten código de barras.");
            RuleFor(x => x.CompareAtPrice)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten precio anterior.");
            RuleFor(x => x.Calories)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten calorías.");
            RuleFor(x => x.Allergens)
                .Must(string.IsNullOrWhiteSpace)
                .WithMessage("Los servicios no admiten alérgenos.");
            RuleFor(x => x.MaxQuantityPerOrder)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten máximo por orden.");
            RuleFor(x => x.IsFeatured)
                .Equal(false)
                .WithMessage("Los servicios no admiten la marca de destacado.");
        });

        When(x => !IsServiceUnit(x.Unit), () =>
        {
            RuleFor(x => x.IsTrackable)
                .Equal(false)
                .WithMessage("Solo los servicios pueden ser rastreables.");
            RuleFor(x => x.NotifyCustomer)
                .Equal(false)
                .WithMessage("Solo los servicios pueden notificar al cliente.");
        });
    }

    private static bool IsServiceUnit(UnitType? unit) =>
        unit is UnitType.Hour or UnitType.Session or UnitType.Service;
}

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateProductCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ProductDto>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        // Options are loaded so the response carries them (the product form keeps editing with it).
        var product = await _context.Products
            .Include(p => p.OptionGroups)
            .ThenInclude(g => g.Options)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            product.TenantId,
            cancellationToken);
        var resultingUnit = request.Unit;

        if (!TenantCapabilityRules.CanManageProductUnit(tenantCapabilities, resultingUnit))
        {
            return Result.Failure<ProductDto>(
                "El negocio no tiene habilitado el módulo requerido para este tipo de artículo.");
        }

        // A product that controls its stock keeps the unit its stock is counted in.
        var stockItem = await _context.SupplyItems.FirstOrDefaultAsync(s => s.ProductId == product.Id, cancellationToken);
        if (stockItem is not null && (request.Unit ?? UnitType.Unit) != stockItem.Unit)
            return Result.Failure<ProductDto>("Este producto controla sus existencias: deja de controlarlas para cambiar su unidad.");

        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null)
            throw new EntityNotFoundException("Category", request.CategoryId);
        if (!category.IsActive)
            return Result.Failure<ProductDto>("La categoría seleccionada no está activa.");

        if (!string.IsNullOrEmpty(request.Sku))
        {
            var skuExists = await _context.Products
                .AnyAsync(p => p.Sku == request.Sku && p.Id != request.Id, cancellationToken);
            if (skuExists)
                return Result.Failure<ProductDto>("Ya existe un producto con ese SKU.");
        }

        product.Update(
            categoryId: request.CategoryId,
            name: request.Name,
            description: request.Description,
            priceAmount: request.PriceAmount,
            priceCurrency: request.PriceCurrency,
            preparationTimeMinutes: request.PreparationTimeMinutes,
            sku: request.Sku,
            barcode: request.Barcode,
            sortOrder: request.SortOrder,
            isFeatured: request.IsFeatured,
            tags: request.Tags,
            compareAtPrice: request.CompareAtPrice,
            taxRate: request.TaxRate,
            calories: request.Calories,
            allergens: request.Allergens,
            maxQuantityPerOrder: request.MaxQuantityPerOrder,
            unit: request.Unit,
            availableFromTime: request.AvailableFromTime,
            availableToTime: request.AvailableToTime,
            isTrackable: request.IsTrackable,
            notifyCustomer: request.NotifyCustomer,
            isTaxExempt: request.IsTaxExempt);
        product.SetFiscalData(request.SatProductCode, request.SatUnitCode, request.TaxObject);
        // Its stock goes by its name in inventory.
        if (stockItem is not null && stockItem.Name != product.Name)
            stockItem.RenameForProduct(product.Name);

        await _context.SaveChangesAsync(cancellationToken);
        var dto = product.ToDto();
        await ProductStockReader.ApplyAsync(_context, dto, cancellationToken);
        return Result.Success(dto);
    }
}
