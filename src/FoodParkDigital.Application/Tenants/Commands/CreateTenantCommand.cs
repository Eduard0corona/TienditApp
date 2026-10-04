using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Tenants.Commands;

[RequiresPlatformAdmin]
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
    private readonly IDateTimeProvider _clock;

    public CreateTenantCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<TenantDto>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant(request.Name, request.Slug, request.BusinessType, request.ContactEmail);

        _context.Tenants.Add(tenant);

        var capabilities = TenantCapabilityDefaults.FromBusinessType(request.BusinessType);
        // New businesses start with prices that include IVA, like those that register themselves.
        var settings = new TenantSettings(tenant.Id, capabilities, pricesIncludeTax: true);
        _context.TenantSettings.Add(settings);

        var publicGeneralCustomer = new Customer(
            tenantId: tenant.Id,
            name: "Publico en general",
            phone: null,
            email: null,
            isPublicGeneral: true);
        _context.Customers.Add(publicGeneralCustomer);

        var defaultBranch = Branch.CreateDefault(tenant.Id);
        _context.Branches.Add(defaultBranch);

        var defaultLocation = new InventoryLocation(
            tenantId: tenant.Id,
            name: "Almacen principal",
            code: "MAIN",
            isDefault: true,
            branchId: defaultBranch.Id);
        _context.InventoryLocations.Add(defaultLocation);

        // With a plan for new businesses the business starts its trial there (modules trimmed to the plan).
        var trial = await SubscriptionRules.StartTrialForNewBusinessAsync(
            _context, tenant.Id, settings, request.BusinessType, _clock.UtcNow, cancellationToken);
        if (trial is not null)
            _context.TenantSubscriptions.Add(trial);

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
            Capabilities = capabilities,
            CreatedAt = tenant.CreatedAt
        });
    }
}
