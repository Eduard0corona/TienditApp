using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.WhatsApp.Media;

/// <summary>A photo or audio a customer sent by WhatsApp (Fase 31L, ADR 0092): for whoever runs the chat, what its API asks.</summary>
[RequiresPermission(TenantPermission.ManageWhatsApp)]
public record GetWhatsAppMediaFileQuery(string Key) : IRequest<Result<PrivateFileDownload>>;

public class GetWhatsAppMediaFileQueryHandler : IRequestHandler<GetWhatsAppMediaFileQuery, Result<PrivateFileDownload>>
{
    private readonly ICurrentTenantService _tenant;
    private readonly IPrivateFileStorage _storage;

    public GetWhatsAppMediaFileQueryHandler(ICurrentTenantService tenant, IPrivateFileStorage storage)
    {
        _tenant = tenant;
        _storage = storage;
    }

    // Another business's file answers like a missing one, so a key reveals nothing about it.
    public async Task<Result<PrivateFileDownload>> Handle(GetWhatsAppMediaFileQuery request, CancellationToken cancellationToken)
    {
        if (!WhatsAppMediaRules.TryParseKey(request.Key, out var tenantId) || tenantId != _tenant.TenantId)
            return Result.Failure<PrivateFileDownload>(GetPrivateOrderFileQueryHandler.NotFoundError);

        var content = await _storage.OpenReadAsync(request.Key, cancellationToken);
        return content is null
            ? Result.Failure<PrivateFileDownload>(GetPrivateOrderFileQueryHandler.NotFoundError)
            : Result.Success(new PrivateFileDownload(content, WhatsAppMediaRules.ContentTypeOf(request.Key), Path.GetFileName(request.Key)));
    }
}
