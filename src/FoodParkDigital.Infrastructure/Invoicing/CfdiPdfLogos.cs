using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Invoicing;

/// <summary>The logo a business puts on the PDF of its CFDI (Fase 28L, ADR 0057), as the PAC takes it.</summary>
public interface ICfdiPdfLogos
{
    /// <summary>
    /// The logo of the business as PNG, or null when it has none or it cannot be read: the PDF goes without it, never late
    /// or missing because of it.
    /// </summary>
    Task<byte[]?> PngAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// The logo of Configuración › Negocio, read from TienditApp's own storage: only from the business's folder (never an
/// address the URL points elsewhere), its medium variant, as PNG without metadata and within the box of the PAC's template.
/// </summary>
internal sealed class TenantCfdiPdfLogos : ICfdiPdfLogos
{
    // The medium variant (512 px wide) is enough for the template's box, and much lighter than the original.
    public const string Variant = "_md.webp";
    public const int MaxWidth = 400;
    public const int MaxHeight = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImageProcessingService _images;
    private readonly ILogger<TenantCfdiPdfLogos> _logger;

    public TenantCfdiPdfLogos(IServiceScopeFactory scopeFactory, ImageProcessingService images, ILogger<TenantCfdiPdfLogos> logger)
    {
        _scopeFactory = scopeFactory;
        _images = images;
        _logger = logger;
    }

    public async Task<byte[]?> PngAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            // The gateway lives for the whole application; the database and the storage, for one scope.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var logoUrl = await context.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.LogoUrl)
                .FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrEmpty(logoUrl))
                return null;

            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            await using var variant = await storage.OpenReadAsync(logoUrl + Variant, TenantStorageFolders.Logos(tenantId), cancellationToken);
            if (variant is null)
            {
                _logger.LogWarning("The logo of business {TenantId} is not in its folder: its CFDI PDF goes without it", tenantId);
                return null;
            }
            return await _images.ToPngAsync(variant, MaxWidth, MaxHeight, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("The logo of business {TenantId} could not be read ({Error}): its CFDI PDF goes without it",
                tenantId, ex.GetType().Name);
            return null;
        }
    }
}
