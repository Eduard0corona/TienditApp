using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Commands;

/// <summary>
/// Adds a photo to a service order (Fase 25, ADR 0024): before or after, with an optional note. The file is one the
/// business uploaded through the app (<see cref="UploadServiceIntakePhotoCommand"/>); who adds it and when come from the
/// session and the clock.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record AddServiceOrderPhotoCommand : IRequest<Result<ServiceOrderPhotoDto>>
{
    public Guid OrderId { get; init; }
    public ServicePhotoStage Stage { get; init; }
    /// <summary>What the upload returned: "/files/{key}" of a photo of this business.</summary>
    public string FileUrl { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? Note { get; init; }
}

public class AddServiceOrderPhotoCommandValidator : AbstractValidator<AddServiceOrderPhotoCommand>
{
    public AddServiceOrderPhotoCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Stage).IsInEnum().WithMessage("La etapa de la foto no es válida.");
        RuleFor(x => x.FileUrl).NotEmpty().WithMessage("Falta la foto.")
            .MaximumLength(ServiceOrderPhoto.MaxFileUrlLength).WithMessage(OrderUploadRules.ServicePhotoError);
        RuleFor(x => x.FileName).MaximumLength(ServiceOrderPhoto.MaxFileNameLength);
        RuleFor(x => x.Note).MaximumLength(ServiceOrderPhoto.MaxNoteLength).WithMessage(Order.NoteTooLongMessage);
    }
}

public class AddServiceOrderPhotoCommandHandler : IRequestHandler<AddServiceOrderPhotoCommand, Result<ServiceOrderPhotoDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly CurrentAccessContext _access;
    private readonly IDateTimeProvider _clock;

    public AddServiceOrderPhotoCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        CurrentAccessContext access,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _access = access;
        _clock = clock;
    }

    public async Task<Result<ServiceOrderPhotoDto>> Handle(AddServiceOrderPhotoCommand request, CancellationToken cancellationToken)
    {
        // Only a photo uploaded through the app for this business, never a link to anything else.
        if (!OrderUploadRules.TryGetOwnFileKey(request.FileUrl, _tenant.TenantId, OrderUploadRules.ServiceIntakeArea, out var key))
            return Result.Failure<ServiceOrderPhotoDto>(OrderUploadRules.ServicePhotoError);

        var remover = await ServiceOrderPhotoViews.RemoverAsync(_currentUser, _access, cancellationToken);

        // Two people can add photos to the same order at once: each one changes the order row, so the second save
        // hits its row version and is tried again with the photos the first one added (and the limit holds).
        return await ServiceOrderPhotoChanges.SaveAsync(_context, request.OrderId, order =>
        {
            var photo = order.AddServicePhoto(
                request.Stage,
                OrderUploadRules.ToPrivateFilePath(key),
                request.FileName,
                request.Note,
                _clock.UtcNow,
                _currentUser.UserName,
                remover.UserId);
            return Result.Success(ServiceOrderPhotoViews.Of(order, photo, remover));
        }, Result.Failure<ServiceOrderPhotoDto>, cancellationToken);
    }
}

/// <summary>
/// Removes a photo added by mistake (Fase 25, ADR 0024) while the order is open: whoever took it, or a manager of the
/// business. The file stays in storage; the order no longer shows it.
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record RemoveServiceOrderPhotoCommand(Guid OrderId, Guid PhotoId) : IRequest<Result>;

public class RemoveServiceOrderPhotoCommandValidator : AbstractValidator<RemoveServiceOrderPhotoCommand>
{
    public RemoveServiceOrderPhotoCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.PhotoId).NotEmpty();
    }
}

public class RemoveServiceOrderPhotoCommandHandler : IRequestHandler<RemoveServiceOrderPhotoCommand, Result>
{
    public const string NotYoursMessage = "Solo quien tomó la foto o un administrador del negocio puede quitarla.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly CurrentAccessContext _access;
    private readonly IDateTimeProvider _clock;

    public RemoveServiceOrderPhotoCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        CurrentAccessContext access,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _access = access;
        _clock = clock;
    }

    public async Task<Result> Handle(RemoveServiceOrderPhotoCommand request, CancellationToken cancellationToken)
    {
        var remover = await ServiceOrderPhotoViews.RemoverAsync(_currentUser, _access, cancellationToken);

        return await ServiceOrderPhotoChanges.SaveAsync(_context, request.OrderId, order =>
        {
            // The order says first why a photo cannot go (it is not there, or the order is closed); then whose it is.
            var photo = order.ServicePhotos.FirstOrDefault(p => p.Id == request.PhotoId);
            if (photo is not null && order.ServicePhotosRemovable && !remover.May(order, photo))
                return Result.Failure(NotYoursMessage);

            order.RemoveServicePhoto(request.PhotoId, _clock.UtcNow);
            return Result.Success();
        }, Result.Failure, cancellationToken);
    }
}

/// <summary>
/// Loads a service order with its photos, applies a change and saves it, trying again with fresh data when another
/// change of the order was saved meanwhile (its row version).
/// </summary>
internal static class ServiceOrderPhotoChanges
{
    private const int MaxAttempts = 3;
    public const string SavedMeanwhileMessage = "La orden cambió mientras guardabas. Vuelve a abrirla e intenta de nuevo.";

    /// <param name="failure">The failed result of the change, with the error to show.</param>
    public static async Task<TResult> SaveAsync<TResult>(
        IApplicationDbContext context,
        Guid orderId,
        Func<Order, TResult> change,
        Func<string, TResult> failure,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var order = await context.Orders
                .Include(o => o.ServicePhotos)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new EntityNotFoundException("Order", orderId);

            TResult result;
            try
            {
                result = change(order);
            }
            catch (DomainException ex)
            {
                return failure(ex.Message);
            }
            if (result.IsFailure)
                return result;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (context is DbContext dbContext)
                    dbContext.ChangeTracker.Clear();
            }
        }

        return failure(SavedMeanwhileMessage);
    }
}
