using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Catalog.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

/// <summary>
/// Fills an empty catalog with a sample one (categories and products) chosen in the guided setup. It is
/// refused once the business has products, so it can never duplicate or overwrite a real catalog.
/// </summary>
[RequiresTenantManager]
public record InstallStarterCatalogCommand : IRequest<Result<InstallStarterCatalogResultDto>>
{
    public string TemplateId { get; init; } = null!;
}

public class InstallStarterCatalogResultDto
{
    public string TemplateId { get; set; } = null!;
    public string TemplateName { get; set; } = null!;
    public int CategoriesCreated { get; set; }
    public int ProductsCreated { get; set; }
}

public class InstallStarterCatalogCommandValidator : AbstractValidator<InstallStarterCatalogCommand>
{
    public InstallStarterCatalogCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty().MaximumLength(100);
    }
}

public class InstallStarterCatalogCommandHandler
    : IRequestHandler<InstallStarterCatalogCommand, Result<InstallStarterCatalogResultDto>>
{
    private const string Currency = "MXN";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public InstallStarterCatalogCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result<InstallStarterCatalogResultDto>> Handle(
        InstallStarterCatalogCommand request,
        CancellationToken cancellationToken)
    {
        if (!TenantManagementRules.CanManageTenant(_currentUserService))
            return Result.Failure<InstallStarterCatalogResultDto>(TenantManagementRules.NotAllowedMessage);

        var template = StarterCatalogTemplates.GetById(request.TemplateId);
        if (template is null)
            return Result.Failure<InstallStarterCatalogResultDto>("Plantilla no encontrada.");

        var tenantId = _tenantService.TenantId;
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, tenantId, cancellationToken);
        var products = template.Categories.SelectMany(category => category.Products).ToList();
        if (products.Any(product => !TenantCapabilityRules.CanManageProductUnit(capabilities, product.Unit)))
        {
            return Result.Failure<InstallStarterCatalogResultDto>(
                "Esta plantilla necesita un módulo que tu negocio no tiene activo. Actívalo en Mi empresa o elige otra plantilla.");
        }

        if (await _context.Products.AnyAsync(cancellationToken))
        {
            return Result.Failure<InstallStarterCatalogResultDto>(
                "Tu catálogo ya tiene productos. Agrega los que falten desde Catálogo.");
        }

        // Categories with the same name are reused (the business may have created them already).
        var categoriesByName = await _context.Categories
            .ToDictionaryAsync(category => category.Name.Trim().ToLowerInvariant(), cancellationToken);

        var categoriesCreated = 0;
        foreach (var categoryDefinition in template.Categories.OrderBy(category => category.SortOrder))
        {
            var key = categoryDefinition.Name.Trim().ToLowerInvariant();
            if (!categoriesByName.TryGetValue(key, out var category))
            {
                category = new Category(tenantId, categoryDefinition.Name, null, categoryDefinition.SortOrder);
                _context.Categories.Add(category);
                categoriesByName[key] = category;
                categoriesCreated++;
            }
            else if (!category.IsActive)
            {
                category.Activate();
            }

            var sortOrder = 0;
            foreach (var productDefinition in categoryDefinition.Products)
            {
                sortOrder += 10;
                _context.Products.Add(new Product(
                    tenantId: tenantId,
                    categoryId: category.Id,
                    name: productDefinition.Name,
                    description: null,
                    priceAmount: productDefinition.Price,
                    priceCurrency: Currency,
                    preparationTimeMinutes: productDefinition.PreparationTimeMinutes,
                    sortOrder: sortOrder,
                    isFeatured: productDefinition.IsFeatured,
                    taxRate: productDefinition.TaxRate,
                    unit: productDefinition.Unit,
                    isTrackable: productDefinition.IsTrackable));
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new InstallStarterCatalogResultDto
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            CategoriesCreated = categoriesCreated,
            ProductsCreated = products.Count
        });
    }
}
