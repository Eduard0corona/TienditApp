using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.DTOs;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.Orders.Commands;

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

public class UploadServiceIntakePhotoCommandHandler : IRequestHandler<UploadServiceIntakePhotoCommand, Result<UploadedFileDto>>
{
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public UploadServiceIntakePhotoCommandHandler(
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadServiceIntakePhotoCommand request, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var folder = $"orders/{_tenantService.TenantId}/service-intake/{DateTime.UtcNow:yyyyMM}";

        var url = await _fileStorageService.UploadAsync(
            request.FileStream,
            fileName,
            request.ContentType,
            folder,
            cancellationToken);

        return Result.Success(new UploadedFileDto
        {
            Url = url,
            FileName = fileName,
            ContentType = request.ContentType
        });
    }
}

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
            .WithMessage("Solo se permiten imagenes o PDF.");
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
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public UploadOrderPaymentEvidenceCommandHandler(
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result<UploadedFileDto>> Handle(UploadOrderPaymentEvidenceCommand request, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{extension}";
        var folder = $"orders/{_tenantService.TenantId}/payment-evidence/{DateTime.UtcNow:yyyyMM}";

        var url = await _fileStorageService.UploadAsync(
            request.FileStream,
            fileName,
            request.ContentType,
            folder,
            cancellationToken);

        return Result.Success(new UploadedFileDto
        {
            Url = url,
            FileName = fileName,
            ContentType = request.ContentType
        });
    }
}
