using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

public record DeleteTenantLogoCommand : IRequest<Result>;

public class DeleteTenantLogoCommandHandler : IRequestHandler<DeleteTenantLogoCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public DeleteTenantLogoCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(DeleteTenantLogoCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role != "TENANT_ADMIN")
            return Result.Failure("Solo el administrador puede eliminar el logo.");

        var tenantId = _tenantService.TenantId;
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure("Tenant no encontrado.");

        if (string.IsNullOrEmpty(tenant.LogoUrl))
            return Result.Success();

        await _fileStorageService.DeleteVariantsAsync(tenant.LogoUrl, cancellationToken);
        tenant.Update(tenant.Name, tenant.ContactEmail, tenant.ContactPhone, null);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
