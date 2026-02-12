using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

public record CreateProductCommand : IRequest<Result<ProductDto>>
{
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public decimal PriceAmount { get; init; }
    public string PriceCurrency { get; init; } = "USD";
    public string? ImageUrl { get; init; }
    public int? PreparationTimeMinutes { get; init; }
}

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PriceAmount).GreaterThan(0);
        RuleFor(x => x.PriceCurrency).NotEmpty().MaximumLength(3);
    }
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateProductCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken)
            ?? throw new EntityNotFoundException("Category", request.CategoryId);

        var product = new Product(
            tenantId: _tenantService.TenantId,
            categoryId: request.CategoryId,
            name: request.Name,
            description: request.Description,
            priceAmount: request.PriceAmount,
            priceCurrency: request.PriceCurrency,
            imageUrl: request.ImageUrl,
            preparationTimeMinutes: request.PreparationTimeMinutes);

        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<ProductDto>(product));
    }
}
