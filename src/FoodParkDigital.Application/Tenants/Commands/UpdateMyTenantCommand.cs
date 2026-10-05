using FoodParkDigital.Application.Billing;
using System;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

[RequiresTenantManager]
public record UpdateMyTenantCommand : IRequest<Result<TenantProfileDto>>
{
    public string Name { get; init; } = null!;
    public string ContactEmail { get; init; } = null!;
    public string? ContactPhone { get; init; }
    public string? Rfc { get; init; }
    public string? LegalName { get; init; }
    public string? Industry { get; init; }
    public string? TaxRegime { get; init; }
    public string? Street { get; init; }
    public string? ExteriorNumber { get; init; }
    public string? InteriorNumber { get; init; }
    public string? Neighborhood { get; init; }
    public string? ZipCode { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public TenantCapability? Capabilities { get; init; }
    /// <summary>IANA id from <see cref="BusinessTimeZones.Supported"/>; null keeps the current one.</summary>
    public string? TimeZone { get; init; }
}

public class UpdateMyTenantCommandValidator : AbstractValidator<UpdateMyTenantCommand>
{
    public UpdateMyTenantCommandValidator()
    {
        const TenantCapability allowedCapabilities =
            TenantCapability.Retail
            | TenantCapability.Service
            | TenantCapability.Food
            | TenantCapability.Inventory
            | TenantCapability.Appointments
            | TenantCapability.Kitchen;

        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.ContactPhone).MaximumLength(20);
        RuleFor(x => x.Rfc)
            .Matches(@"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$")
            .When(x => !string.IsNullOrEmpty(x.Rfc))
            .WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.Industry).MaximumLength(200);
        RuleFor(x => x.TaxRegime).MaximumLength(200);
        RuleFor(x => x.Street).MaximumLength(300);
        RuleFor(x => x.ExteriorNumber).MaximumLength(20);
        RuleFor(x => x.InteriorNumber).MaximumLength(20);
        RuleFor(x => x.Neighborhood).MaximumLength(200);
        RuleFor(x => x.ZipCode).MaximumLength(10);
        RuleFor(x => x.City).MaximumLength(200);
        RuleFor(x => x.State).MaximumLength(100);
        RuleFor(x => x.Capabilities)
            .Must(c => !c.HasValue || (c.Value & ~allowedCapabilities) == 0)
            .WithMessage("Las capacidades proporcionadas no son validas.");
        RuleFor(x => x.TimeZone)
            .Must(BusinessTimeZones.IsSupported)
            .When(x => x.TimeZone is not null)
            .WithMessage("Elige una zona horaria de la lista.");
        // Like its plan, a business always sells something.
        RuleFor(x => x.Capabilities)
            .Must(c => !c.HasValue || TenantCapabilityRules.HasCommerce(c.Value))
            .WithMessage("Elige al menos un tipo de venta: productos, servicios o alimentos.");
    }
}

public class UpdateMyTenantCommandHandler : IRequestHandler<UpdateMyTenantCommand, Result<TenantProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public UpdateMyTenantCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<TenantProfileDto>> Handle(UpdateMyTenantCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(
            _context,
            _tenantService,
            _currentUserService,
            cancellationToken);

        if (tenantId is null)
            return Result.Failure<TenantProfileDto>("No se encontró el negocio del usuario.");

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant is null)
            return Result.Failure<TenantProfileDto>("Negocio no encontrado.");

        tenant.Update(request.Name, request.ContactEmail, request.ContactPhone, tenant.LogoUrl);

        Address? address = null;
        if (!string.IsNullOrEmpty(request.Street) && !string.IsNullOrEmpty(request.ExteriorNumber))
        {
            address = new Address(
                request.Street,
                request.ExteriorNumber,
                request.InteriorNumber,
                request.Neighborhood ?? "",
                request.ZipCode ?? "",
                request.City ?? "",
                request.State ?? "");
        }

        tenant.SetBusinessInfo(request.Rfc, request.LegalName, request.Industry, request.TaxRegime, address);

        if (request.Capabilities is { } requestedModules)
        {
            var planError = await SubscriptionRules.CheckModulesAsync(_context, tenant.Id, requestedModules, cancellationToken);
            if (planError is not null)
                return Result.Failure<TenantProfileDto>(planError);
        }

        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id, cancellationToken);
        var previousCapabilities = settings?.Capabilities;
        var capabilitiesChanged = false;

        if (settings is null)
        {
            var resolvedCapabilities = request.Capabilities ?? TenantCapabilityDefaults.FromBusinessType(tenant.BusinessType);
            settings = new Domain.Tenants.Entities.TenantSettings(
                tenant.Id,
                resolvedCapabilities);
            _context.TenantSettings.Add(settings);
            capabilitiesChanged = request.Capabilities.HasValue;
        }
        else if (request.Capabilities.HasValue)
        {
            capabilitiesChanged = settings.Capabilities != request.Capabilities.Value;
            settings.UpdateCapabilities(request.Capabilities.Value);
        }

        if (request.TimeZone is not null && settings.TimeZone != request.TimeZone)
            settings.SetTimeZone(request.TimeZone);

        if (capabilitiesChanged)
        {
            var auditLog = new AuditLog(
                tenantId: tenant.Id,
                entityName: "TenantSettings",
                entityId: tenant.Id.ToString(),
                action: "TenantCapabilitiesUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    PreviousCapabilities = previousCapabilities?.ToString(),
                    NewCapabilities = settings.Capabilities.ToString()
                }));
            _context.AuditLogs.Add(auditLog);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var dto = tenant.ToProfileDto();
        dto.Capabilities = settings.Capabilities;
        dto.PricesIncludeTax = settings.PricesIncludeTax;
        dto.DefaultTaxRate = settings.DefaultTaxRate;
        dto.AutoCloseCounterSales = settings.AutoCloseCounterSales;
        dto.RequireOpenCashSession = settings.RequireOpenCashSession;
        dto.OrderNotificationPolicy = settings.OrderNotificationPolicy;
        dto.InsufficientStockPolicy = settings.InsufficientStockPolicy;
        dto.OnboardingCompletedAt = settings.OnboardingCompletedAt;
        dto.TimeZone = settings.TimeZone;
        return Result.Success(dto);
    }
}
