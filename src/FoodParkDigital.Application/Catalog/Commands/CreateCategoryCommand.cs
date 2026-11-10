using FoodParkDigital.Application.Catalog.DTOs;
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
public record CreateCategoryCommand : IRequest<Result<CategoryDto>>
{
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        // The same rules as editing it: what could not be saved by an edit must not get in by creating it.
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateCategoryCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        if (await CategoryNames.FindTakenAsync(_context, request.Name, null, cancellationToken) is { } taken)
            return Result.Failure<CategoryDto>(taken);

        if (await _context.Categories.CountAsync(cancellationToken) >= Category.MaxCategoriesPerTenant)
            return Result.Failure<CategoryDto>(CategoryLimits.Reached);

        var category = new Category(
            _tenantService.TenantId,
            request.Name,
            request.Description,
            request.SortOrder);

        _context.Categories.Add(category);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another save took the name at the same moment (the unique index): this one is undone, and the person is
            // told which category has it.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await CategoryNames.FindTakenAsync(_context, request.Name, null, cancellationToken) is { } meanwhile)
                return Result.Failure<CategoryDto>(meanwhile);
            throw;
        }

        return Result.Success(category.ToDto());
    }
}

internal static class CategoryLimits
{
    public static readonly string Reached =
        $"Un negocio puede tener como máximo {Category.MaxCategoriesPerTenant} categorías (también cuentan las archivadas).";
}
