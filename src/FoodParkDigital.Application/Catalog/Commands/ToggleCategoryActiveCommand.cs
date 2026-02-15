using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

public record ToggleCategoryActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class ToggleCategoryActiveCommandValidator : AbstractValidator<ToggleCategoryActiveCommand>
{
    public ToggleCategoryActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class ToggleCategoryActiveCommandHandler : IRequestHandler<ToggleCategoryActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public ToggleCategoryActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(ToggleCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Category", request.Id);

        if (request.IsActive)
            category.Activate();
        else
            category.Deactivate();

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
