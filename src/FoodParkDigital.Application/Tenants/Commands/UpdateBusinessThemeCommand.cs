using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
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
/// The colors of the business for its whole team and its public menu (owners and admins). Only which theme is
/// stored: the client paints it, and the colors of states (ready, pending, cancelled) never change with it.
/// </summary>
[RequiresTenantManager]
public record UpdateBusinessThemeCommand(BusinessTheme Theme) : IRequest<Result<BusinessThemeDto>>;

public class UpdateBusinessThemeCommandValidator : AbstractValidator<UpdateBusinessThemeCommand>
{
    public UpdateBusinessThemeCommandValidator()
    {
        RuleFor(x => x.Theme).IsInEnum().WithMessage("El tema no existe.");
    }
}

public class UpdateBusinessThemeCommandHandler : IRequestHandler<UpdateBusinessThemeCommand, Result<BusinessThemeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;

    public UpdateBusinessThemeCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
    }

    public async Task<Result<BusinessThemeDto>> Handle(UpdateBusinessThemeCommand request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure<BusinessThemeDto>("No se encontró el negocio del usuario.");

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
                return Result.Failure<BusinessThemeDto>("Negocio no encontrado.");

            settings = new TenantSettings(tenantId.Value, TenantCapabilityDefaults.FromBusinessType(businessType.Value));
            _context.TenantSettings.Add(settings);
        }

        var previous = settings.Theme;
        settings.SetTheme(request.Theme);
        if (previous != settings.Theme)
        {
            // Customers see it on the public menu: who changed the look of the business, and from what, stays recorded.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: tenantId.Value,
                entityName: nameof(TenantSettings),
                entityId: tenantId.Value.ToString(),
                action: "BusinessThemeUpdated",
                performedBy: _currentUserService.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { Previous = previous.ToString(), New = settings.Theme.ToString() })));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(new BusinessThemeDto { Theme = settings.Theme });
    }
}
