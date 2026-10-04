using AutoMapper;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

/// <summary>
/// Replaces the variants and extras of a product. Groups and options sent with their id keep it; the ones
/// left out are removed (lines already sold keep their own copy).
/// </summary>
[RequiresPermission(TenantPermission.ManageCatalog)]
public record SetProductOptionGroupsCommand : IRequest<Result<ProductDto>>
{
    public Guid ProductId { get; init; }
    public List<ProductOptionGroupInput> Groups { get; init; } = new();
}

public record ProductOptionGroupInput
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = null!;
    public int MinSelections { get; init; }
    public int MaxSelections { get; init; } = 1;
    public List<ProductOptionInput> Options { get; init; } = new();
}

public record ProductOptionInput
{
    public Guid? Id { get; init; }
    public string Name { get; init; } = null!;
    public decimal PriceDelta { get; init; }
    public bool IsAvailable { get; init; } = true;
}

public class SetProductOptionGroupsCommandValidator : AbstractValidator<SetProductOptionGroupsCommand>
{
    public SetProductOptionGroupsCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Groups).NotNull();
        RuleFor(x => x.Groups.Count).LessThanOrEqualTo(Product.MaxOptionGroups)
            .WithMessage($"Un producto admite como máximo {Product.MaxOptionGroups} grupos de opciones.");
        RuleForEach(x => x.Groups).ChildRules(group =>
        {
            group.RuleFor(g => g.Name).NotEmpty().MaximumLength(ProductOptionGroup.MaxNameLength)
                .WithMessage($"Cada grupo necesita un nombre de máximo {ProductOptionGroup.MaxNameLength} caracteres.");
            group.RuleFor(g => g.MinSelections).GreaterThanOrEqualTo(0);
            group.RuleFor(g => g.MaxSelections).GreaterThanOrEqualTo(1);
            group.RuleFor(g => g).Must(g => g.MinSelections <= g.MaxSelections)
                .WithMessage("El mínimo de opciones no puede ser mayor al máximo.");
            group.RuleFor(g => g.Options).NotEmpty().WithMessage("Cada grupo necesita al menos una opción.");
            group.RuleFor(g => g.Options.Count).LessThanOrEqualTo(ProductOptionGroup.MaxOptions)
                .WithMessage($"Un grupo admite como máximo {ProductOptionGroup.MaxOptions} opciones.");
            group.RuleForEach(g => g.Options).ChildRules(option =>
            {
                option.RuleFor(o => o.Name).NotEmpty().MaximumLength(ProductOption.MaxNameLength)
                    .WithMessage($"Cada opción necesita un nombre de máximo {ProductOption.MaxNameLength} caracteres.");
                option.RuleFor(o => o.PriceDelta)
                    .InclusiveBetween(-ProductOption.MaxPriceDelta, ProductOption.MaxPriceDelta)
                    .PrecisionScale(18, 2, ignoreTrailingZeros: true);
            });
        });
    }
}

public class SetProductOptionGroupsCommandHandler : IRequestHandler<SetProductOptionGroupsCommand, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public SetProductOptionGroupsCommandHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(SetProductOptionGroupsCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .Include(p => p.OptionGroups)
            .ThenInclude(g => g.Options)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.ProductId);

        product.SetOptionGroups(request.Groups
            .Select(group => new ProductOptionGroupDefinition(
                group.Id,
                group.Name,
                group.MinSelections,
                group.MaxSelections,
                group.Options.Select(option => new ProductOptionDefinition(option.Id, option.Name, option.PriceDelta, option.IsAvailable)).ToList()))
            .ToList());

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(_mapper.Map<ProductDto>(product));
    }
}
