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

[RequiresPermission(TenantPermission.ManageCatalog)]
public record UpdateCategoryCommand : IRequest<Result<CategoryDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Result<CategoryDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateCategoryCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CategoryDto>> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Category", request.Id);

        // Only a new name is checked: a category repeated before the rule can still change its description or order.
        if (Category.NameKey(request.Name) != Category.NameKey(category.Name)
            && await CategoryNames.FindTakenAsync(_context, request.Name, category.Id, cancellationToken) is { } taken)
            return Result.Failure<CategoryDto>(taken);

        category.Update(request.Name, request.Description, request.SortOrder);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another category took the name at the same moment (the unique index): this edit is undone, and the person
            // is told which one has it.
            await _context.DiscardUnitOfWorkAsync(cancellationToken);
            if (await CategoryNames.FindTakenAsync(_context, request.Name, request.Id, cancellationToken) is { } meanwhile)
                return Result.Failure<CategoryDto>(meanwhile);
            throw;
        }

        return Result.Success(category.ToDto());
    }
}
