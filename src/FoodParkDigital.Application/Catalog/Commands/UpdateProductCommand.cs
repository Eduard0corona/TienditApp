using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
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
    }
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

        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken);
        if (!categoryExists)
            throw new EntityNotFoundException("Category", request.CategoryId);

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
            imageUrl: product.ImageUrl,
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
            unit: request.Unit);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(_mapper.Map<ProductDto>(product));
    }
}
