using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
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
    public string? ImageUrl { get; init; }
    public int? PreparationTimeMinutes { get; init; }
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
        RuleFor(x => x.ImageUrl).MaximumLength(500);
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

        product.Update(
            request.CategoryId,
            request.Name,
            request.Description,
            request.PriceAmount,
            request.PriceCurrency,
            request.ImageUrl,
            request.PreparationTimeMinutes);

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(_mapper.Map<ProductDto>(product));
    }
}
