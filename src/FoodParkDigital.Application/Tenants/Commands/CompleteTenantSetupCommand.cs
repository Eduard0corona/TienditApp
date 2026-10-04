using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Utils;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>Marks the guided setup as finished (or skipped) so it is not offered again.</summary>
public record CompleteTenantSetupCommand : IRequest<Result>;

public class CompleteTenantSetupCommandHandler : IRequestHandler<CompleteTenantSetupCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CompleteTenantSetupCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(CompleteTenantSetupCommand request, CancellationToken cancellationToken)
    {
        if (!TenantManagementRules.CanManageTenant(_currentUserService))
            return Result.Failure(TenantManagementRules.NotAllowedMessage);

        var tenantId = await MyTenantResolver.ResolveAsync(_context, _tenantService, _currentUserService, cancellationToken);
        if (tenantId is null)
            return Result.Failure("No se encontro el negocio del usuario.");

        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId.Value, cancellationToken);
        if (settings is null)
            return Result.Failure("El negocio no tiene configuración. Guarda primero los datos de Mi empresa.");

        settings.CompleteOnboarding(_dateTimeProvider.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
