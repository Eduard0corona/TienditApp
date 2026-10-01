using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

public record UploadAvatarCommand : IRequest<Result<AvatarImageUrlsDto>>
{
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadAvatarCommandValidator : AbstractValidator<UploadAvatarCommand>
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public UploadAvatarCommandValidator()
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

public class UploadAvatarCommandHandler : IRequestHandler<UploadAvatarCommand, Result<AvatarImageUrlsDto>>
{
    private const string OriginalVariantSuffix = "_original.webp";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImageProcessingService _imageProcessingService;

    public UploadAvatarCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService,
        IImageProcessingService imageProcessingService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
        _imageProcessingService = imageProcessingService;
    }

    public async Task<Result<AvatarImageUrlsDto>> Handle(UploadAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result.Failure<AvatarImageUrlsDto>("Usuario no autenticado.");

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Result.Failure<AvatarImageUrlsDto>("Usuario no encontrado.");

        var previousAvatarUrl = user.AvatarUrl;

        // Generar variantes de imagen
        var variants = await _imageProcessingService.GenerateVariantsAsync(
            request.FileStream, cancellationToken);

        // Subir cada variante
        var folder = $"avatars/{_tenantService.TenantId}";
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
            return Result.Failure<AvatarImageUrlsDto>("No se pudo almacenar la imagen.");

        // Actualizar avatar del usuario
        user.AvatarUrl = baseUrl;
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            await _fileStorageService.DeleteVariantsAsync(baseUrl, cancellationToken);
            return Result.Failure<AvatarImageUrlsDto>("No se pudo actualizar el avatar.");
        }

        // Las variantes anteriores se eliminan solo cuando la nueva imagen ya quedo guardada; antes se
        // borraban primero y una imagen rechazada dejaba al usuario apuntando a archivos inexistentes.
        if (!string.IsNullOrEmpty(previousAvatarUrl))
            await _fileStorageService.DeleteVariantsAsync(previousAvatarUrl, cancellationToken);

        return Result.Success(AvatarImageUrlsDto.FromBaseUrl(baseUrl)!);
    }
}
