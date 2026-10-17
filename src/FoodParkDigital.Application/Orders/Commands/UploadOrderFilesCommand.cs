using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// A photo of a service: with the order when it is received, or added later (Fase 25) by whoever sees the orders or
/// works the service board.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record UploadServiceIntakePhotoCommand : IRequest<Result<UploadedFileDto>>
{
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadServiceIntakePhotoCommandValidator : AbstractValidator<UploadServiceIntakePhotoCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxFileSizeBytes = 8 * 1024 * 1024;

    public UploadServiceIntakePhotoCommandValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(x => x.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage("El archivo debe ser una imagen.");
        RuleFor(x => x.FileSize)
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("La imagen no puede exceder 8 MB.");
        RuleFor(x => x.FileName)
            .Must(fn => AllowedExtensions.Contains(Path.GetExtension(fn).ToLowerInvariant()))
            .WithMessage($"Extensiones permitidas: {string.Join(", ", AllowedExtensions)}");
    }
}

/// <summary>Photos of what a customer leaves for service: private to the business, like its receipts.</summary>
public class UploadServiceIntakePhotoCommandHandler : IRequestHandler<UploadServiceIntakePhotoCommand, Result<UploadedFileDto>>
{
    private const int MaxPhotoDimension = 2048;

    private readonly ICurrentTenantService _tenantService;
    private readonly IPrivateFileStorage _storage;
    private readonly IImageNormalizationService _imageNormalizationService;

    public UploadServiceIntakePhotoCommandHandler(
        ICurrentTenantService tenantService,
        IPrivateFileStorage storage,
        IImageNormalizationService imageNormalizationService)
    {
        _tenantService = tenantService;
        _storage = storage;
        _imageNormalizationService = imageNormalizationService;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadServiceIntakePhotoCommand request, CancellationToken cancellationToken)
    {
        // The type comes from the content, never from the file name or the client's Content-Type.
        var content = await OrderUploadRules.BufferAsync(request.FileStream, cancellationToken);
        var kind = await FileSignature.DetectAsync(content, cancellationToken);
        if (!FileSignature.IsImage(kind))
            return Result.Failure<UploadedFileDto>("El archivo no es una imagen JPG, PNG o WebP valida.");

        Stream photo;
        try
        {
            // Re-encoding drops anything hidden in the file and the photo's metadata (GPS, device).
            photo = await _imageNormalizationService.NormalizeToWebpAsync(content, MaxPhotoDimension, cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<UploadedFileDto>(ex.Message);
        }

        await using (photo)
        {
            var fileName = $"{Guid.NewGuid()}.webp";
            var key = $"{OrderUploadRules.ServiceIntakeFolder(_tenantService.TenantId, DateTimeOffset.UtcNow)}/{fileName}";
            await _storage.SaveAsync(key, photo, "image/webp", cancellationToken);

            return Result.Success(new UploadedFileDto
            {
                Url = OrderUploadRules.ToPrivateFilePath(key),
                FileName = fileName,
                ContentType = "image/webp"
            });
        }
    }
}

/// <summary>The transfer receipt of a payment: part of charging, like registering the payment itself.</summary>
[RequiresPermission(TenantPermission.ChargeOrders)]
public record UploadOrderPaymentEvidenceCommand : IRequest<Result<UploadedFileDto>>
{
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadOrderPaymentEvidenceCommandValidator : AbstractValidator<UploadOrderPaymentEvidenceCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".pdf"];
    private const long MaxFileSizeBytes = 12 * 1024 * 1024;

    public UploadOrderPaymentEvidenceCommandValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(ct =>
                ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Solo se permiten imágenes o PDF.");
        RuleFor(x => x.FileSize)
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("El archivo no puede exceder 12 MB.");
        RuleFor(x => x.FileName)
            .Must(fn => AllowedExtensions.Contains(Path.GetExtension(fn).ToLowerInvariant()))
            .WithMessage($"Extensiones permitidas: {string.Join(", ", AllowedExtensions)}");
    }
}

public class UploadOrderPaymentEvidenceCommandHandler : IRequestHandler<UploadOrderPaymentEvidenceCommand, Result<UploadedFileDto>>
{
    private const int MaxImageDimension = 2048;

    private readonly ICurrentTenantService _tenantService;
    private readonly IPrivateFileStorage _storage;
    private readonly IImageNormalizationService _imageNormalizationService;

    public UploadOrderPaymentEvidenceCommandHandler(
        ICurrentTenantService tenantService,
        IPrivateFileStorage storage,
        IImageNormalizationService imageNormalizationService)
    {
        _tenantService = tenantService;
        _storage = storage;
        _imageNormalizationService = imageNormalizationService;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadOrderPaymentEvidenceCommand request, CancellationToken cancellationToken)
    {
        // The type comes from the content, never from the file name or the client's Content-Type.
        var content = await OrderUploadRules.BufferAsync(request.FileStream, cancellationToken);
        var kind = await FileSignature.DetectAsync(content, cancellationToken);
        content.Position = 0;

        Stream stored;
        string extension;
        string contentType;
        if (FileSignature.IsImage(kind))
        {
            try
            {
                stored = await _imageNormalizationService.NormalizeToWebpAsync(content, MaxImageDimension, cancellationToken);
            }
            catch (DomainException ex)
            {
                return Result.Failure<UploadedFileDto>(ex.Message);
            }

            extension = ".webp";
            contentType = "image/webp";
        }
        else if (kind == UploadedFileKind.Pdf)
        {
            // PDFs cannot be re-encoded here; they are stored as-is and only downloaded, never rendered by the API.
            stored = content;
            extension = ".pdf";
            contentType = "application/pdf";
        }
        else
        {
            return Result.Failure<UploadedFileDto>("Solo se permiten imágenes JPG, PNG o WebP, o documentos PDF.");
        }

        await using (stored)
        {
            var fileName = $"{Guid.NewGuid()}{extension}";
            var key = $"{OrderUploadRules.PaymentEvidenceFolder(_tenantService.TenantId, DateTimeOffset.UtcNow)}/{fileName}";
            await _storage.SaveAsync(key, stored, contentType, cancellationToken);

            return Result.Success(new UploadedFileDto
            {
                Url = OrderUploadRules.ToPrivateFilePath(key),
                FileName = fileName,
                ContentType = contentType
            });
        }
    }
}
