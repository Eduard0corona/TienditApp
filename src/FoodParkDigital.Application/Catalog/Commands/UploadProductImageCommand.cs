using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

public record UploadProductImageCommand : IRequest<Result<ProductImageUrlsDto>>
{
    public Guid ProductId { get; init; }
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public UploadProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
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

public class UploadProductImageCommandHandler : IRequestHandler<UploadProductImageCommand, Result<ProductImageUrlsDto>>
{
    private const string OriginalVariantSuffix = "_original.webp";

    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImageProcessingService _imageProcessingService;
    private readonly ICurrentTenantService _tenantService;

    public UploadProductImageCommandHandler(
        IApplicationDbContext context,
        IFileStorageService fileStorageService,
        IImageProcessingService imageProcessingService,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _imageProcessingService = imageProcessingService;
        _tenantService = tenantService;
    }

    public async Task<Result<ProductImageUrlsDto>> Handle(UploadProductImageCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.ProductId);

        // Eliminar variantes anteriores si existen
        if (!string.IsNullOrEmpty(product.ImageUrl))
            await _fileStorageService.DeleteVariantsAsync(product.ImageUrl, cancellationToken);

        // Generar variantes de imagen
        var variants = await _imageProcessingService.GenerateVariantsAsync(
            request.FileStream, cancellationToken);

        // Subir cada variante
        var folder = $"products/{_tenantService.TenantId}";
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

        // Actualizar producto con la ruta base (sin sufijo ni extensión)
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Result.Failure<ProductImageUrlsDto>("No se pudo almacenar la imagen.");

        product.SetImage(baseUrl);

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(ProductImageUrlsDto.FromBaseUrl(baseUrl)!);
    }
}
