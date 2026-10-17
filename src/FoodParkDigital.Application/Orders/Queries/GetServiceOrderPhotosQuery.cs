using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>
/// The photos of a service order (Fase 25, ADR 0024): for whoever sees the orders or works the service board, like the
/// files of the photos themselves (<see cref="GetServicePhotoFileQuery"/>).
/// </summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
[RequiresCapability(TenantCapability.Service)]
public record GetServiceOrderPhotosQuery(Guid OrderId) : IRequest<Result<ServiceOrderPhotosDto>>;

public class GetServiceOrderPhotosQueryHandler : IRequestHandler<GetServiceOrderPhotosQuery, Result<ServiceOrderPhotosDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly CurrentAccessContext _access;

    public GetServiceOrderPhotosQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser, CurrentAccessContext access)
    {
        _context = context;
        _currentUser = currentUser;
        _access = access;
    }

    public async Task<Result<ServiceOrderPhotosDto>> Handle(GetServiceOrderPhotosQuery request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePhotos)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new EntityNotFoundException("Order", request.OrderId);
        if (order.OrderType != OrderType.Service)
            return Result.Failure<ServiceOrderPhotosDto>(Order.PhotosOnlyForServicesMessage);

        var remover = await ServiceOrderPhotoViews.RemoverAsync(_currentUser, _access, cancellationToken);
        return Result.Success(ServiceOrderPhotoViews.Of(order, remover));
    }
}

/// <summary>Who is asking to remove a photo: their account, and whether they manage the business.</summary>
public readonly record struct ServicePhotoRemover(Guid? UserId, bool IsManager)
{
    /// <summary>
    /// Whether they may remove <paramref name="photo"/> of <paramref name="order"/>: while the order is open, the
    /// photo they took, or any photo for a manager. The evidence one person took is not another's to remove.
    /// </summary>
    public bool May(Order order, ServiceOrderPhoto photo)
        => order.ServicePhotosRemovable && (IsManager || (UserId is { } id && photo.TakenByUserId == id));
}

/// <summary>The photos of a service order as its team sees them: those taken when it was received, then the added ones.</summary>
public static class ServiceOrderPhotoViews
{
    public static async Task<ServicePhotoRemover> RemoverAsync(
        ICurrentUserService currentUser,
        CurrentAccessContext access,
        CancellationToken cancellationToken)
        => new(
            Guid.TryParse(currentUser.UserId, out var userId) ? userId : null,
            await access.IsTenantManagerAsync(cancellationToken));

    public static ServiceOrderPhotosDto Of(Order order, ServicePhotoRemover remover)
    {
        var atIntake = new[]
            {
                (Stage: ServicePhotoStage.Before, Json: order.ServiceDetail?.BeforePhotosJson),
                (Stage: ServicePhotoStage.After, Json: order.ServiceDetail?.AfterPhotosJson)
            }
            .SelectMany(kept => OrderUploadRules.ReadServicePhotos(kept.Json, order.TenantId).Select(photo => new ServiceOrderPhotoDto
            {
                Stage = kept.Stage,
                Url = photo.Url,
                FileName = photo.FileName,
                AtIntake = true
            }));

        return new ServiceOrderPhotosDto
        {
            OrderId = order.Id,
            CanAdd = order.TakesServicePhotos(ServicePhotoStage.After),
            CanAddBefore = order.TakesServicePhotos(ServicePhotoStage.Before),
            Photos = atIntake
                .Concat(order.ServicePhotos.OrderBy(p => p.TakenAt).ThenBy(p => p.Id).Select(p => Of(order, p, remover)))
                .ToList()
        };
    }

    public static ServiceOrderPhotoDto Of(Order order, ServiceOrderPhoto photo, ServicePhotoRemover remover) => new()
    {
        Id = photo.Id,
        Stage = photo.Stage,
        Url = photo.FileUrl,
        FileName = photo.FileName,
        Note = photo.Note,
        TakenAt = photo.TakenAt,
        TakenBy = photo.TakenBy,
        CanRemove = remover.May(order, photo)
    };
}
