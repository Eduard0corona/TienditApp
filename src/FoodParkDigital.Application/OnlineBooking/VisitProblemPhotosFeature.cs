using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>
/// The customer sends a photo of what happens with the visit they requested online (Fase 29H, ADR 0062), with the link of the
/// request, without an account: up to <see cref="HomeVisitRules.MaxProblemPhotos"/> while the visit waits or is agendada,
/// before its time. The photo is read for what it is (never its name or the type the browser says), saved again as WebP
/// without its metadata and kept private for the business. The answer is the appointment as its customer sees it, with how
/// many photos it has; a link unknown or expired is not found (null), as when it is opened.
/// </summary>
[AllowAnonymousRequest("Una foto de lo que pasa para la visita que el cliente pidió, con el enlace imposible de adivinar de su solicitud: se guarda privada para el negocio, hasta cinco.")]
public record AddVisitProblemPhotoCommand : IRequest<Result<CustomerAppointmentDto>?>
{
    public string Token { get; init; } = null!;
    public Stream FileStream { get; init; } = null!;
    public long FileSize { get; init; }
}

public class AddVisitProblemPhotoCommandValidator : AbstractValidator<AddVisitProblemPhotoCommand>
{
    public AddVisitProblemPhotoCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FileStream).NotNull();
        RuleFor(x => x.FileSize)
            .InclusiveBetween(1, VisitProblemPhotos.MaxFileBytes)
            .WithMessage(VisitProblemPhotos.TooLargeMessage);
    }
}

public class AddVisitProblemPhotoCommandHandler : IRequestHandler<AddVisitProblemPhotoCommand, Result<CustomerAppointmentDto>?>
{
    // Two photos sent at once read the same count: the base keeps the place once, and the second takes the next one.
    private const int Attempts = 3;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly IPrivateFileStorage _storage;
    private readonly IImageNormalizationService _images;
    private readonly PublicPhotoGate _gate;

    public AddVisitProblemPhotoCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider clock,
        IPrivateFileStorage storage,
        IImageNormalizationService images,
        PublicPhotoGate gate)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _storage = storage;
        _images = images;
        _gate = gate;
    }

    public async Task<Result<CustomerAppointmentDto>?> Handle(AddVisitProblemPhotoCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var found = await PublicAppointmentLink.FindAsync(_context, request.Token, now, cancellationToken);
        if (found is null)
            return null;

        var (appointmentId, tenantId) = found.Value;
        _tenantService.SetTenant(tenantId);
        // Read, never tracked: a photo changes nothing of the appointment, so it never conflicts with the business deciding it.
        var appointment = await _context.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId, cancellationToken);
        if (appointment is null)
            return null;

        // Refused before the file is read: a visit that takes no more photos costs nothing.
        var photos = await CountAsync(appointmentId, cancellationToken);
        if (appointment.ProblemPhotoRefusal(photos, now) is { } refusal)
            return Result.Failure<CustomerAppointmentDto>(refusal);

        string key;
        using (var slot = await _gate.EnterAsync(cancellationToken))
        {
            if (slot is null)
                return Result.Failure<CustomerAppointmentDto>(VisitProblemPhotos.BusyMessage);

            // The type comes from the content, never from the file name or the browser's Content-Type.
            var content = await OrderUploadRules.BufferAsync(request.FileStream, cancellationToken);
            if (!FileSignature.IsImage(await FileSignature.DetectAsync(content, cancellationToken)))
                return Result.Failure<CustomerAppointmentDto>(VisitProblemPhotos.NotAPhotoMessage);

            Stream photo;
            try
            {
                // Re-encoding drops anything hidden in the file and the photo's metadata (where it was taken, the device).
                photo = await _images.NormalizeToWebpAsync(content, VisitProblemPhotos.MaxDimension, cancellationToken);
            }
            catch (DomainException ex)
            {
                return Result.Failure<CustomerAppointmentDto>(ex.Message);
            }

            await using (photo)
            {
                key = $"{OrderUploadRules.VisitProblemFolder(tenantId, now)}/{Guid.NewGuid()}.webp";
                await _storage.SaveAsync(key, photo, "image/webp", cancellationToken);
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            AppointmentProblemPhoto added;
            try
            {
                added = appointment.AddProblemPhoto(photos, key, now);
            }
            catch (DomainException ex)
            {
                // Full meanwhile: the file stays in the business's private storage, never linked (ADR 0062, limits).
                return Result.Failure<CustomerAppointmentDto>(ex.Message);
            }

            _context.AppointmentProblemPhotos.Add(added);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt < Attempts)
            {
                _context.AppointmentProblemPhotos.Remove(added);
                var counted = await CountAsync(appointmentId, cancellationToken);
                // Not its place taken by another photo: something else failed, and it is not tried again.
                if (counted == photos)
                    throw;
                photos = counted;
            }
        }

        return Result.Success((await CustomerAppointments.ReadAsync(_context, appointmentId, now, cancellationToken))!);
    }

    private Task<int> CountAsync(Guid appointmentId, CancellationToken cancellationToken)
        => _context.AppointmentProblemPhotos.CountAsync(p => p.AppointmentId == appointmentId, cancellationToken);

}

/// <summary>
/// A photo of what happens that a customer sent with a visit (Fase 29H): for whoever works the agenda or quotes the visit.
/// Only a file a photo of the business keeps opens; any other key answers like a missing file.
/// </summary>
[RequiresPermission(TenantPermission.ManageAppointments, TenantPermission.ManageOrders)]
[RequiresCapability(TenantCapability.Appointments | TenantCapability.Service)]
public record GetVisitProblemPhotoFileQuery(string Key) : IRequest<Result<PrivateFileDownload>>;

public class GetVisitProblemPhotoFileQueryHandler : IRequestHandler<GetVisitProblemPhotoFileQuery, Result<PrivateFileDownload>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IPrivateFileStorage _storage;

    public GetVisitProblemPhotoFileQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant, IPrivateFileStorage storage)
    {
        _context = context;
        _tenant = tenant;
        _storage = storage;
    }

    public async Task<Result<PrivateFileDownload>> Handle(GetVisitProblemPhotoFileQuery request, CancellationToken cancellationToken)
    {
        if (!OrderUploadRules.TryParsePrivateKey(request.Key, out var tenantId, out var area)
            || area != OrderUploadRules.VisitProblemArea
            || tenantId != _tenant.TenantId
            || !await _context.AppointmentProblemPhotos.AnyAsync(p => p.FileKey == request.Key, cancellationToken))
            return Result.Failure<PrivateFileDownload>(GetPrivateOrderFileQueryHandler.NotFoundError);

        var content = await _storage.OpenReadAsync(request.Key, cancellationToken);
        return content is null
            ? Result.Failure<PrivateFileDownload>(GetPrivateOrderFileQueryHandler.NotFoundError)
            : Result.Success(new PrivateFileDownload(content, "image/webp", Path.GetFileName(request.Key)));
    }
}

/// <summary>The photos of what happens of the visits a customer requested online (Fase 29H, ADR 0062).</summary>
public static class VisitProblemPhotos
{
    /// <summary>The largest photo taken, as the phone makes it.</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;
    /// <summary>The longest side it is kept with.</summary>
    public const int MaxDimension = 2048;

    public const string TooLargeMessage = "Cada foto puede pesar hasta 10 MB.";
    public const string NotAPhotoMessage = "Manda una foto JPG, PNG o WebP.";
    public const string BusyMessage = "Estamos recibiendo muchas fotos. Intenta de nuevo en un momento.";

    /// <summary>The photos of these appointments of the business in context, as the paths that open them, in the order sent.</summary>
    internal static async Task<Dictionary<Guid, List<string>>> PathsAsync(
        IApplicationDbContext context, IEnumerable<Guid> appointmentIds, CancellationToken cancellationToken)
    {
        var ids = appointmentIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var photos = await context.AppointmentProblemPhotos
            .AsNoTracking()
            .Where(p => ids.Contains(p.AppointmentId))
            .OrderBy(p => p.Position)
            .Select(p => new { p.AppointmentId, p.FileKey })
            .ToListAsync(cancellationToken);
        return photos
            .GroupBy(p => p.AppointmentId)
            .ToDictionary(g => g.Key, g => g.Select(p => OrderUploadRules.ToPrivateFilePath(p.FileKey)).ToList());
    }
}

/// <summary>
/// At most <see cref="AtOnce"/> photos from customers without an account decoded at the same time in this instance: decoding
/// a large photo takes a lot of memory, and anyone can send one. One more waits up to <see cref="Wait"/>; after that it is
/// refused and the customer tries again.
/// </summary>
public sealed class PublicPhotoGate
{
    public const int AtOnce = 2;
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _wait;

    public PublicPhotoGate() : this(AtOnce, Wait) { }

    public PublicPhotoGate(int atOnce, TimeSpan wait)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(atOnce, 1);
        _slots = new SemaphoreSlim(atOnce, atOnce);
        _wait = wait;
    }

    /// <returns>The slot, to dispose once the photo is saved; null when none freed up in time.</returns>
    public async Task<IDisposable?> EnterAsync(CancellationToken cancellationToken)
        => await _slots.WaitAsync(_wait, cancellationToken) ? new Slot(_slots) : null;

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }
}
