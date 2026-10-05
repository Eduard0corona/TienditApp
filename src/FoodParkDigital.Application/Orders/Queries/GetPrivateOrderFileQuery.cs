using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Orders.Queries;

/// <summary>A private file opened by a member of its business.</summary>
public sealed record PrivateFileDownload(Stream Content, string ContentType, string FileName);

/// <summary>A transfer receipt: for whoever sees the payments of the orders or charges them.</summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ChargeOrders)]
public record GetPaymentEvidenceFileQuery(string Key) : IRequest<Result<PrivateFileDownload>>;

/// <summary>A photo of a service: for whoever sees the orders or works the service board.</summary>
[RequiresPermission(TenantPermission.ManageOrders, TenantPermission.ManageServiceBoard)]
public record GetServicePhotoFileQuery(string Key) : IRequest<Result<PrivateFileDownload>>;

public class GetPrivateOrderFileQueryHandler :
    IRequestHandler<GetPaymentEvidenceFileQuery, Result<PrivateFileDownload>>,
    IRequestHandler<GetServicePhotoFileQuery, Result<PrivateFileDownload>>
{
    public const string NotFoundError = "El archivo no existe.";

    private readonly ICurrentTenantService _tenant;
    private readonly IPrivateFileStorage _storage;

    public GetPrivateOrderFileQueryHandler(ICurrentTenantService tenant, IPrivateFileStorage storage)
    {
        _tenant = tenant;
        _storage = storage;
    }

    public Task<Result<PrivateFileDownload>> Handle(GetPaymentEvidenceFileQuery request, CancellationToken cancellationToken)
        => OpenAsync(request.Key, OrderUploadRules.PaymentEvidenceArea, cancellationToken);

    public Task<Result<PrivateFileDownload>> Handle(GetServicePhotoFileQuery request, CancellationToken cancellationToken)
        => OpenAsync(request.Key, OrderUploadRules.ServiceIntakeArea, cancellationToken);

    // Another business's file answers like a missing one, so a key reveals nothing about it.
    private async Task<Result<PrivateFileDownload>> OpenAsync(string key, string area, CancellationToken cancellationToken)
    {
        if (!OrderUploadRules.TryParsePrivateKey(key, out var tenantId, out var keyArea)
            || keyArea != area
            || tenantId != _tenant.TenantId)
            return Result.Failure<PrivateFileDownload>(NotFoundError);

        var content = await _storage.OpenReadAsync(key, cancellationToken);
        return content is null
            ? Result.Failure<PrivateFileDownload>(NotFoundError)
            : Result.Success(new PrivateFileDownload(content, OrderUploadRules.ContentTypeOf(key), Path.GetFileName(key)));
    }
}
