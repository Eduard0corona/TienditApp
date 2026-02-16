using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Tenants.Commands;

public record CreateTenantCommand : IRequest<Result<TenantDto>>
{
    public string Name { get; init; } = null!;
    public string Slug { get; init; } = null!;
    public BusinessType BusinessType { get; init; }
    public string ContactEmail { get; init; } = null!;
    public string? ContactPhone { get; init; }
}

public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.BusinessType).IsInEnum();
    }
}

public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, Result<TenantDto>>
{
    private readonly IApplicationDbContext _context;

    public CreateTenantCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<TenantDto>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant(request.Name, request.Slug, request.BusinessType, request.ContactEmail);

        _context.Tenants.Add(tenant);

        var settings = new TenantSettings(tenant.Id);
        _context.TenantSettings.Add(settings);

        var publicGeneralCustomer = new Customer(
            tenantId: tenant.Id,
            name: "Publico en general",
            phone: null,
            email: null,
            isPublicGeneral: true);
        _context.Customers.Add(publicGeneralCustomer);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantDto
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Slug = tenant.Slug,
            BusinessType = tenant.BusinessType,
            Status = tenant.Status,
            ContactEmail = tenant.ContactEmail,
            ContactPhone = request.ContactPhone,
            CreatedAt = tenant.CreatedAt
        });
    }
}
