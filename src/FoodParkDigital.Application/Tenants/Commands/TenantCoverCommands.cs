using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Commands;

/// <summary>
/// The cover of the online menu (Fase 24E, ADR 0026): a wide photo across the top of the menu, stored in the same
/// variants as the logo (the widths keep its shape) in its own public folder. Owners and admins, like the logo.
/// </summary>
[RequiresTenantManager]
public record UploadTenantCoverCommand : IRequest<Result<LogoImageUrlsDto>>
{
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadTenantCoverCommandValidator : AbstractValidator<UploadTenantCoverCommand>
{
    // The same files as the logo: the image is decoded when its variants are made, whatever its name says.
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public UploadTenantCoverCommandValidator()
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

public class UploadTenantCoverCommandHandler : IRequestHandler<UploadTenantCoverCommand, Result<LogoImageUrlsDto>>
{
    private const string OriginalVariantSuffix = "_original.webp";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImageProcessingService _imageProcessingService;

    public UploadTenantCoverCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService,
        IImageProcessingService imageProcessingService)
    {
        _context = context;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
        _imageProcessingService = imageProcessingService;
    }

    public async Task<Result<LogoImageUrlsDto>> Handle(UploadTenantCoverCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure<LogoImageUrlsDto>("Negocio no encontrado.");

        var folder = TenantStorageFolders.Covers(tenantId);
        var previousCoverUrl = tenant.CoverImageUrl;

        var variants = await _imageProcessingService.GenerateVariantsAsync(request.FileStream, cancellationToken);
        var baseId = Guid.NewGuid().ToString();
        string? baseUrl = null;
        try
        {
            foreach (var variant in variants)
            {
                var uploadedUrl = await _fileStorageService.UploadAsync(
                    variant.Stream,
                    $"{baseId}{variant.Suffix}.webp",
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

        tenant.SetCoverImage(baseUrl);
        await _context.SaveChangesAsync(cancellationToken);

        // The previous files go only once the new cover is saved, as with the logo: a rejected image keeps the old one.
        if (!string.IsNullOrEmpty(previousCoverUrl))
            await _fileStorageService.DeleteVariantsAsync(previousCoverUrl, folder, cancellationToken);

        return Result.Success(LogoImageUrlsDto.FromBaseUrl(baseUrl)!);
    }
}

/// <summary>Removes the cover of the online menu: the menu shows the colors of the business again.</summary>
[RequiresTenantManager]
public record DeleteTenantCoverCommand : IRequest<Result>;

public class DeleteTenantCoverCommandHandler : IRequestHandler<DeleteTenantCoverCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public DeleteTenantCoverCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _context = context;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(DeleteTenantCoverCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return Result.Failure("Negocio no encontrado.");

        if (string.IsNullOrEmpty(tenant.CoverImageUrl))
            return Result.Success();

        var previousCoverUrl = tenant.CoverImageUrl;
        tenant.SetCoverImage(null);
        await _context.SaveChangesAsync(cancellationToken);
        await _fileStorageService.DeleteVariantsAsync(previousCoverUrl, TenantStorageFolders.Covers(tenantId), cancellationToken);

        return Result.Success();
    }
}
