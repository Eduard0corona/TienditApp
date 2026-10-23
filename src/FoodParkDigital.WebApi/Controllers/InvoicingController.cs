using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.WebApi.Errors;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Controllers;

/// <summary>
/// Invoicing (Fase 28A, ADR 0045): Configuración › Facturación (the business's CSD and serie, for owners and admins) and
/// the CFDI 4.0 invoice of an order, stamped through the platform's PAC, downloaded and cancelled; and the global invoice
/// of a period to the general public (Fase 28B, ADR 0046), for owners and admins.
/// </summary>
[ApiController]
[Route("api/v1/invoicing")]
[Authorize]
public class InvoicingController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvoicingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<InvoicingSettingsDto>> GetSettings(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetInvoicingSettingsQuery(), cancellationToken));

    /// <summary>Uploads the business's CSD (.cer and .key in base64, with the key's password); it replaces the one before.</summary>
    [HttpPut("settings/certificate")]
    // Two files of 16 KB (what the reader takes) in base64 and the password fit in 64 KB.
    [RequestSizeLimit(64 * 1024)]
    public async Task<ActionResult<InvoicingSettingsDto>> UploadCertificate(
        [FromBody] UploadInvoicingCertificateRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UploadInvoicingCertificateCommand(request.Certificate, request.PrivateKey, request.Password), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpDelete("settings/certificate")]
    public async Task<ActionResult<InvoicingSettingsDto>> RemoveCertificate(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemoveInvoicingCertificateCommand(), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpPut("settings/serie")]
    public async Task<ActionResult<InvoicingSettingsDto>> ChangeSerie(
        [FromBody] ChangeInvoiceSerieRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChangeInvoiceSerieCommand(request.Serie), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Whether the order can be invoiced, whom to, and its invoices.</summary>
    [HttpGet("orders/{orderId:guid}")]
    public async Task<ActionResult<OrderInvoicingDto>> GetOrderInvoicing(Guid orderId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetOrderInvoicingQuery(orderId), cancellationToken));

    /// <summary>
    /// Stamps the invoice of the order. It answers the invoice as it ended up: stamped, refused by the PAC (with its reason),
    /// or still stamping when the PAC did not answer (it is asked again).
    /// </summary>
    [HttpPost("orders/{orderId:guid}/invoices")]
    public async Task<ActionResult<OrderInvoiceDto>> IssueInvoice(
        Guid orderId, [FromBody] IssueOrderInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new IssueOrderInvoiceCommand(orderId, request.Receiver, request.SaveToCustomer), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Asks the PAC again for an invoice still stamping, with the same CFDI.</summary>
    [HttpPost("invoices/{invoiceId:guid}/retry")]
    public async Task<ActionResult<OrderInvoiceDto>> RetryInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RetryOrderInvoiceCommand(invoiceId), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Cancels a stamped invoice with the SAT (motive 02 or 03), owners and admins.</summary>
    [HttpPost("invoices/{invoiceId:guid}/cancel")]
    public async Task<ActionResult<OrderInvoiceDto>> CancelInvoice(
        Guid invoiceId, [FromBody] CancelOrderInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelOrderInvoiceCommand(invoiceId, request.Motive), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpGet("invoices/{invoiceId:guid}/xml")]
    public Task<IActionResult> DownloadXml(Guid invoiceId, CancellationToken cancellationToken)
        => DownloadAsync(new GetOrderInvoiceFileQuery(invoiceId, OrderInvoiceFileKind.Xml), cancellationToken);

    [HttpGet("invoices/{invoiceId:guid}/pdf")]
    public Task<IActionResult> DownloadPdf(Guid invoiceId, CancellationToken cancellationToken)
        => DownloadAsync(new GetOrderInvoiceFileQuery(invoiceId, OrderInvoiceFileKind.Pdf), cancellationToken);

    /// <summary>
    /// The business's global invoices, the newest period first; the search finds one by its folio, its UUID or the folio of
    /// a sale it covers.
    /// </summary>
    [HttpGet("global-invoices")]
    public async Task<ActionResult<PaginatedList<GlobalInvoiceDto>>> GetGlobalInvoices(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetGlobalInvoicesQuery(pageNumber, pageSize, search), cancellationToken));

    /// <summary>
    /// The global invoice of the period of that periodicity that holds the day, before it is stamped: its sales, the ones
    /// left out and why, its amounts and what stops it.
    /// </summary>
    [HttpGet("global-invoices/draft")]
    public async Task<ActionResult<GlobalInvoiceDraftDto>> GetGlobalInvoiceDraft(
        [FromQuery] GlobalInvoicePeriodicity periodicity, [FromQuery] DateOnly day, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetGlobalInvoiceDraftQuery(periodicity, day), cancellationToken));

    /// <summary>
    /// Stamps the global invoice of a period that is over, if its sales are still the ones reviewed (their number and total).
    /// It answers the invoice as it ended up: stamped, refused by the PAC (with its reason), or still stamping.
    /// </summary>
    [HttpPost("global-invoices")]
    public async Task<ActionResult<GlobalInvoiceDto>> IssueGlobalInvoice(
        [FromBody] IssueGlobalInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new IssueGlobalInvoiceCommand(request.Periodicity, request.Day, request.ExpectedSaleCount, request.ExpectedTotal),
            cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>A global invoice with the sales it covers.</summary>
    [HttpGet("global-invoices/{invoiceId:guid}")]
    public async Task<ActionResult<GlobalInvoiceDetailDto>> GetGlobalInvoice(Guid invoiceId, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetGlobalInvoiceQuery(invoiceId), cancellationToken));

    /// <summary>Asks the PAC again for a global invoice still stamping, with the same CFDI.</summary>
    [HttpPost("global-invoices/{invoiceId:guid}/retry")]
    public async Task<ActionResult<GlobalInvoiceDto>> RetryGlobalInvoice(Guid invoiceId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RetryGlobalInvoiceCommand(invoiceId), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    /// <summary>Cancels a stamped global invoice with the SAT (motive 02 or 04).</summary>
    [HttpPost("global-invoices/{invoiceId:guid}/cancel")]
    public async Task<ActionResult<GlobalInvoiceDto>> CancelGlobalInvoice(
        Guid invoiceId, [FromBody] CancelOrderInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelGlobalInvoiceCommand(invoiceId, request.Motive), cancellationToken);
        return result.IsFailure ? this.ApiError(StatusCodes.Status400BadRequest, result.Error) : Ok(result.Value);
    }

    [HttpGet("global-invoices/{invoiceId:guid}/xml")]
    public Task<IActionResult> DownloadGlobalXml(Guid invoiceId, CancellationToken cancellationToken)
        => DownloadAsync(new GetGlobalInvoiceFileQuery(invoiceId, OrderInvoiceFileKind.Xml), cancellationToken);

    [HttpGet("global-invoices/{invoiceId:guid}/pdf")]
    public Task<IActionResult> DownloadGlobalPdf(Guid invoiceId, CancellationToken cancellationToken)
        => DownloadAsync(new GetGlobalInvoiceFileQuery(invoiceId, OrderInvoiceFileKind.Pdf), cancellationToken);

    private async Task<IActionResult> DownloadAsync(IRequest<Result<PrivateFileDownload>> query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status404NotFound, result.Error);

        var file = result.Value!;
        // Downloaded, never rendered as a page of this origin nor kept by shared caches.
        Response.Headers.ContentSecurityPolicy = "sandbox";
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public class UploadInvoicingCertificateRequest
{
    /// <summary>The .cer file, in base64.</summary>
    public string Certificate { get; init; } = string.Empty;
    /// <summary>The .key file, in base64.</summary>
    public string PrivateKey { get; init; } = string.Empty;
    /// <summary>The private key's password; it is never kept.</summary>
    public string Password { get; init; } = string.Empty;
}

public class ChangeInvoiceSerieRequest
{
    public string Serie { get; init; } = string.Empty;
}

public class IssueOrderInvoiceRequest
{
    public CfdiReceiverInput Receiver { get; init; } = new();
    /// <summary>Keep these fiscal data in the customer with the order's phone, for their next invoices.</summary>
    public bool SaveToCustomer { get; init; }
}

public class IssueGlobalInvoiceRequest
{
    public GlobalInvoicePeriodicity Periodicity { get; init; }
    /// <summary>A day of the period.</summary>
    public DateOnly Day { get; init; }
    /// <summary>How many sales the reviewed preview had.</summary>
    public int ExpectedSaleCount { get; init; }
    /// <summary>The total the reviewed preview had.</summary>
    public decimal ExpectedTotal { get; init; }
}

public class CancelOrderInvoiceRequest
{
    public CfdiCancellationMotive Motive { get; init; }
}
