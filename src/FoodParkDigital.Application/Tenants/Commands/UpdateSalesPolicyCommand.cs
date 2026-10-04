using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>
/// Changes how the business prices and closes its sales. It applies to sales made from now on: every
/// order keeps the policy it was sold with, so no past total changes.
/// </summary>
public record UpdateSalesPolicyCommand : IRequest<Result<TenantSalesPolicyDto>>
{
    public bool PricesIncludeTax { get; init; }
    public decimal DefaultTaxRate { get; init; }
    public bool AutoCloseCounterSales { get; init; }
}

public class UpdateSalesPolicyCommandValidator : AbstractValidator<UpdateSalesPolicyCommand>
{
    public UpdateSalesPolicyCommandValidator()
    {
        RuleFor(x => x.DefaultTaxRate)
            .Must(VatRateRules.IsAllowed)
            .WithMessage(VatRateRules.InvalidRateMessage);
    }
}

public class UpdateSalesPolicyCommandHandler : IRequestHandler<UpdateSalesPolicyCommand, Result<TenantSalesPolicyDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public UpdateSalesPolicyCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<TenantSalesPolicyDto>> Handle(UpdateSalesPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!TenantManagementRules.CanManageTenant(_currentUserService))
            return Result.Failure<TenantSalesPolicyDto>(TenantManagementRules.NotAllowedMessage);

        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<TenantSalesPolicyDto>("No se encontro el negocio del usuario.");

        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value, cancellationToken);
        if (settings is null)
        {
            // Settings missing (businesses from before they existed): start from what the business had.
            var businessType = await _context.Tenants
                .Where(t => t.Id == tenantId.Value)
                .Select(t => (BusinessType?)t.BusinessType)
                .FirstOrDefaultAsync(cancellationToken);
            if (businessType is null)
                return Result.Failure<TenantSalesPolicyDto>("Negocio no encontrado.");

            settings = new TenantSettings(tenantId.Value, TenantCapabilityDefaults.FromBusinessType(businessType.Value));
            _context.TenantSettings.Add(settings);
        }

        var previous = new
        {
            settings.PricesIncludeTax,
            settings.DefaultTaxRate,
            settings.AutoCloseCounterSales
        };
        settings.UpdateSalesPolicy(request.PricesIncludeTax, request.DefaultTaxRate, request.AutoCloseCounterSales);

        var changed = previous.PricesIncludeTax != settings.PricesIncludeTax
            || previous.DefaultTaxRate != settings.DefaultTaxRate
            || previous.AutoCloseCounterSales != settings.AutoCloseCounterSales;
        if (changed)
        {
            // How totals are computed is a financial setting: who changed it, and from what, stays recorded.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId.Value,
                entityName: nameof(TenantSettings),
                entityId: tenantId.Value.ToString(),
                action: "TenantSalesPolicyUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new
                {
                    Previous = previous,
                    New = new
                    {
                        settings.PricesIncludeTax,
                        settings.DefaultTaxRate,
                        settings.AutoCloseCounterSales
                    }
                })));
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new TenantSalesPolicyDto
        {
            PricesIncludeTax = settings.PricesIncludeTax,
            DefaultTaxRate = settings.DefaultTaxRate,
            AutoCloseCounterSales = settings.AutoCloseCounterSales
        });
    }
}
