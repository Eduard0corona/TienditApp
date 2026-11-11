using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments.OnlineBooking;

/// <summary>
/// The photo customers see of a person the business books online (Fase 31I, ADR 0089), instead of their initial: stored in
/// the variants of the business's images, in its own public folder. The booking pages show the smallest one.
/// </summary>
public static class OnlineBookingStaffPhotos
{
    public const string NotConfiguredMessage = "Guarda primero a esta persona en Quién atiende; luego ponle su foto.";

    /// <summary>The variant the pages show, from its base URL; null without a photo.</summary>
    public static string? Url(string? baseUrl) => LogoImageUrlsDto.FromBaseUrl(baseUrl)?.Thumbnail;
}

public class OnlineBookingStaffPhotoDto
{
    /// <summary>The photo customers see now; null without one.</summary>
    public string? PhotoUrl { get; set; }
}

/// <summary>Puts or replaces the photo customers see of a person the business books online (owners and admins).</summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record UploadOnlineBookingStaffPhotoCommand : IRequest<Result<OnlineBookingStaffPhotoDto>>
{
    public Guid UserId { get; init; }
    public Stream FileStream { get; init; } = null!;
    public string FileName { get; init; } = null!;
    public string ContentType { get; init; } = null!;
    public long FileSize { get; init; }
}

public class UploadOnlineBookingStaffPhotoCommandValidator : AbstractValidator<UploadOnlineBookingStaffPhotoCommand>
{
    // The same files as the logo and the cover: the image is decoded when its variants are made, whatever its name says.
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public UploadOnlineBookingStaffPhotoCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
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

/// <summary>Takes away the photo customers see of a person: the pages show their initial again (owners and admins).</summary>
[RequiresTenantManager]
[RequiresCapability(TenantCapability.Appointments)]
public record DeleteOnlineBookingStaffPhotoCommand(Guid UserId) : IRequest<Result<OnlineBookingStaffPhotoDto>>;

public class OnlineBookingStaffPhotoHandler :
    IRequestHandler<UploadOnlineBookingStaffPhotoCommand, Result<OnlineBookingStaffPhotoDto>>,
    IRequestHandler<DeleteOnlineBookingStaffPhotoCommand, Result<OnlineBookingStaffPhotoDto>>
{
    private const string OriginalVariantSuffix = "_original.webp";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IImageProcessingService _imageProcessingService;

    public OnlineBookingStaffPhotoHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorageService,
        IImageProcessingService imageProcessingService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _fileStorageService = fileStorageService;
        _imageProcessingService = imageProcessingService;
    }

    public async Task<Result<OnlineBookingStaffPhotoDto>> Handle(UploadOnlineBookingStaffPhotoCommand request, CancellationToken cancellationToken)
    {
        // The people of this business only: the filter by business finds no one else's.
        var staff = await _context.OnlineBookingStaff.FirstOrDefaultAsync(s => s.UserId == request.UserId, cancellationToken);
        if (staff is null)
            return Result.Failure<OnlineBookingStaffPhotoDto>(OnlineBookingStaffPhotos.NotConfiguredMessage);

        var tenantId = _tenantService.TenantId;
        var folder = TenantStorageFolders.StaffPhotos(tenantId);
        var previous = staff.PhotoUrl;

        var variants = await _imageProcessingService.GenerateVariantsAsync(request.FileStream, cancellationToken);
        var baseId = Guid.NewGuid().ToString();
        string? baseUrl = null;
        try
        {
            foreach (var variant in variants)
            {
                var uploadedUrl = await _fileStorageService.UploadAsync(
                    variant.Stream, $"{baseId}{variant.Suffix}.webp", "image/webp", folder, cancellationToken);
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
            return Result.Failure<OnlineBookingStaffPhotoDto>("No se pudo almacenar la imagen.");

        staff.SetPhoto(baseUrl);
        Record(staff, "OnlineBookingStaffPhotoChanged", previous);
        await _context.SaveChangesAsync(cancellationToken);

        // The previous files go only once the new photo is saved, as with the cover: a rejected image keeps the old one.
        if (!string.IsNullOrEmpty(previous))
            await _fileStorageService.DeleteVariantsAsync(previous, folder, cancellationToken);

        return Result.Success(new OnlineBookingStaffPhotoDto { PhotoUrl = OnlineBookingStaffPhotos.Url(baseUrl) });
    }

    public async Task<Result<OnlineBookingStaffPhotoDto>> Handle(DeleteOnlineBookingStaffPhotoCommand request, CancellationToken cancellationToken)
    {
        var staff = await _context.OnlineBookingStaff.FirstOrDefaultAsync(s => s.UserId == request.UserId, cancellationToken);
        if (staff is null)
            return Result.Failure<OnlineBookingStaffPhotoDto>(OnlineBookingStaffPhotos.NotConfiguredMessage);
        if (string.IsNullOrEmpty(staff.PhotoUrl))
            return Result.Success(new OnlineBookingStaffPhotoDto());

        var previous = staff.PhotoUrl;
        staff.SetPhoto(null);
        Record(staff, "OnlineBookingStaffPhotoRemoved", previous);
        await _context.SaveChangesAsync(cancellationToken);
        await _fileStorageService.DeleteVariantsAsync(previous, TenantStorageFolders.StaffPhotos(_tenantService.TenantId), cancellationToken);
        return Result.Success(new OnlineBookingStaffPhotoDto());
    }

    /// <summary>What strangers see of the team is the owner's decision, as the name: worth recording.</summary>
    private void Record(OnlineBookingStaff staff, string action, string? previous)
        => _context.AuditLogs.Add(new AuditLog(
            tenantId: staff.TenantId,
            entityName: nameof(OnlineBookingStaff),
            entityId: staff.Id.ToString(),
            action: action,
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { staff.UserId, Previous = previous, New = staff.PhotoUrl })));
}
