using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

[RequiresTenantManager]
public record DeleteTenantLogoCommand : IRequest<Result>;

public class DeleteTenantLogoCommandHandler : IRequestHandler<DeleteTenantLogoCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public DeleteTenantLogoCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _context = context;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(DeleteTenantLogoCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure("Negocio no encontrado.");

        if (string.IsNullOrEmpty(tenant.LogoUrl))
            return Result.Success();

        await _fileStorageService.DeleteVariantsAsync(
            tenant.LogoUrl,
            TenantStorageFolders.Logos(tenantId),
            cancellationToken);
        tenant.Update(tenant.Name, tenant.ContactEmail, tenant.ContactPhone, null);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
