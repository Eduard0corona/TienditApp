using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

[RequiresPermission(TenantPermission.ManageCatalog)]
public record InstallCategoryTemplateCommand : IRequest<Result<InstallCategoryTemplateResultDto>>
{
    public string TemplateId { get; init; } = null!;
}

public class InstallCategoryTemplateCommandValidator : AbstractValidator<InstallCategoryTemplateCommand>
{
    public InstallCategoryTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateId)
            .NotEmpty()
            .MaximumLength(100);
    }
}

public class InstallCategoryTemplateCommandHandler : IRequestHandler<InstallCategoryTemplateCommand, Result<InstallCategoryTemplateResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public InstallCategoryTemplateCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<InstallCategoryTemplateResultDto>> Handle(InstallCategoryTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = CategoryTemplateCatalog.GetById(request.TemplateId);
        if (template is null)
            return Result.Failure<InstallCategoryTemplateResultDto>("Plantilla no encontrada.");

        var existingNames = await _context.Categories
            .AsNoTracking()
            .Select(x => x.Name)
            .ToListAsync(cancellationToken);

        var normalizedNames = new HashSet<string>(
            existingNames.Select(NormalizeCategoryName),
            StringComparer.Ordinal);

        var toCreate = new List<Category>();
        foreach (var item in template.Categories.OrderBy(x => x.SortOrder))
        {
            var normalizedName = NormalizeCategoryName(item.Name);
            if (!normalizedNames.Add(normalizedName))
                continue;

            toCreate.Add(new Category(
                _tenantService.TenantId,
                item.Name,
                item.Description,
                item.SortOrder));
        }

        // The whole template or nothing: half of it would leave the business with categories it did not choose.
        if (existingNames.Count + toCreate.Count > Category.MaxCategoriesPerTenant)
            return Result.Failure<InstallCategoryTemplateResultDto>(CategoryLimits.Reached);

        var createdCount = toCreate.Count;
        if (createdCount > 0)
        {
            _context.Categories.AddRange(toCreate);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var total = template.Categories.Count;
        return Result.Success(new InstallCategoryTemplateResultDto
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            CreatedCount = createdCount,
            SkippedCount = total - createdCount,
            TotalCount = total
        });
    }

    private static string NormalizeCategoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            ' ',
            value.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToLowerInvariant();
    }
}
