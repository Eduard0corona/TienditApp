using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Private files of the business (transfer receipts and service photos), streamed to members with the right
/// permission. The client fetches them with its session; they are never public links.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{**key}")]
    public async Task<IActionResult> Get(string key, CancellationToken cancellationToken)
    {
        if (!OrderUploadRules.TryParsePrivateKey(key, out _, out var area))
            return this.ApiError(StatusCodes.Status404NotFound, GetPrivateOrderFileQueryHandler.NotFoundError);

        IRequest<Result<PrivateFileDownload>> query = area == OrderUploadRules.PaymentEvidenceArea
            ? new GetPaymentEvidenceFileQuery(key)
            : new GetServicePhotoFileQuery(key);
        var result = await _mediator.Send(query, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);

        var file = result.Value!;
        // Never rendered as a page of this origin, never kept by shared caches; a PDF is only downloaded.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        Response.Headers.CacheControl = "private, no-store";
        return file.ContentType == "application/pdf"
            ? File(file.Content, file.ContentType, file.FileName)
            : File(file.Content, file.ContentType);
    }
}
