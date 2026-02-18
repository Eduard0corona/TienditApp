using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

public record UploadTenantLogoCommand : IRequest<Result<LogoImageUrlsDto>>
{
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadTenantLogoCommandValidator : AbstractValidator<UploadTenantLogoCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public UploadTenantLogoCommandValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).NotEmpty()
            .Must(ct => ct.StartsWith("image/"))
            .WithMessage("El archivo debe ser una imagen.");
        RuleFor(x => x.FileSize)
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("El archivo no puede exceder 5 MB.");
        RuleFor(x => x.FileName)
            .Must(fn => AllowedExtensions.Contains(Path.GetExtension(fn).ToLowerInvariant()))
            .WithMessage($"Extensiones permitidas: {string.Join(", ", AllowedExtensions)}");
    }
}

public class UploadTenantLogoCommandHandler : IRequestHandler<UploadTenantLogoCommand, Result<LogoImageUrlsDto>>
{
    private const string OriginalVariantSuffix = "_original.webp";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImageProcessingService _imageProcessingService;

    public UploadTenantLogoCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService,
        IImageProcessingService imageProcessingService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
        _imageProcessingService = imageProcessingService;
    }

    public async Task<Result<LogoImageUrlsDto>> Handle(UploadTenantLogoCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role != "TENANT_ADMIN")
            return Result.Failure<LogoImageUrlsDto>("Solo el administrador puede cambiar el logo.");

        var tenantId = _tenantService.TenantId;
        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure<LogoImageUrlsDto>("Tenant no encontrado.");

        // Delete previous variants if they exist
        if (!string.IsNullOrEmpty(tenant.LogoUrl))
            await _fileStorageService.DeleteVariantsAsync(tenant.LogoUrl, cancellationToken);

        // Generate image variants
        var variants = await _imageProcessingService.GenerateVariantsAsync(
            request.FileStream, cancellationToken);

        // Upload each variant
        var folder = $"logos/{tenantId}";
        var baseId = Guid.NewGuid().ToString();
        string? baseUrl = null;

        try
        {
            foreach (var variant in variants)
            {
                var fileName = $"{baseId}{variant.Suffix}.webp";
                var uploadedUrl = await _fileStorageService.UploadAsync(
                    variant.Stream,
                    fileName,
                    "image/webp",
                    folder,
                    cancellationToken);

                if (variant.Suffix == "_original" && uploadedUrl.EndsWith(OriginalVariantSuffix, StringComparison.OrdinalIgnoreCase))
                    baseUrl = uploadedUrl[..^OriginalVariantSuffix.Length];
            }
        }
        finally
        {
            foreach (var variant in variants)
                variant.Dispose();
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result.Failure<LogoImageUrlsDto>("No se pudo almacenar la imagen.");

        // Update tenant logo
        tenant.Update(tenant.Name, tenant.ContactEmail, tenant.ContactPhone, baseUrl);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(LogoImageUrlsDto.FromBaseUrl(baseUrl)!);
    }
}
