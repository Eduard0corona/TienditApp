using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

public record UpdateProductCommand : IRequest<Result<ProductDto>>
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public decimal PriceAmount { get; init; }
    public string PriceCurrency { get; init; } = "USD";
    public int? PreparationTimeMinutes { get; init; }
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public int SortOrder { get; init; }
    public bool IsFeatured { get; init; }
    public string? Tags { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public decimal? TaxRate { get; init; }
    public int? Calories { get; init; }
    public string? Allergens { get; init; }
    public int? StockQuantity { get; init; }
    public int? LowStockThreshold { get; init; }
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
        RuleFor(x => x.TaxRate)
            .InclusiveBetween(0, 100).When(x => x.TaxRate.HasValue);
        RuleFor(x => x.Calories)
            .GreaterThanOrEqualTo(0).When(x => x.Calories.HasValue);
        RuleFor(x => x.StockQuantity)
            .GreaterThanOrEqualTo(0).When(x => x.StockQuantity.HasValue);
        RuleFor(x => x.LowStockThreshold)
            .GreaterThanOrEqualTo(0).When(x => x.LowStockThreshold.HasValue);
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
            RuleFor(x => x.TaxRate)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten tasa de impuesto.");
            RuleFor(x => x.Calories)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten calorías.");
            RuleFor(x => x.Allergens)
                .Must(string.IsNullOrWhiteSpace)
                .WithMessage("Los servicios no admiten alérgenos.");
            RuleFor(x => x.StockQuantity)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten stock.");
            RuleFor(x => x.LowStockThreshold)
                .Must(v => !v.HasValue)
                .WithMessage("Los servicios no admiten alerta de stock.");
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
    private readonly IMapper _mapper;

    public UpdateProductCommandHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
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
                "El tenant no tiene habilitado el modulo requerido para este tipo de item.");
        }

        var category = await _context.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (category is null)
            throw new EntityNotFoundException("Category", request.CategoryId);
        if (!category.IsActive)
            return Result.Failure<ProductDto>("La categoria seleccionada no esta activa.");

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
            stockQuantity: request.StockQuantity,
            lowStockThreshold: request.LowStockThreshold,
            maxQuantityPerOrder: request.MaxQuantityPerOrder,
            unit: request.Unit,
            availableFromTime: request.AvailableFromTime,
            availableToTime: request.AvailableToTime,
            isTrackable: request.IsTrackable,
            notifyCustomer: request.NotifyCustomer);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(_mapper.Map<ProductDto>(product));
    }
}
