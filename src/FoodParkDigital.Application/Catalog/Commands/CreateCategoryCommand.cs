using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

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
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
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
        var category = new Category(
            _tenantService.TenantId,
            request.Name,
            request.Description,
            request.SortOrder);

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(category.ToDto());
    }
}
