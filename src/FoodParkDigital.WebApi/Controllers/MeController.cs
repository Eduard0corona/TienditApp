using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Application.Tenants.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly IMediator _mediator;

    public MeController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Role, permissions and modules of the signed-in user in the current business, resolved by the server.</summary>
    [HttpGet("access")]
    public async Task<ActionResult<MyAccessDto>> GetMyAccess(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyAccessQuery(), cancellationToken));

    [HttpGet("tenants")]
    public async Task<ActionResult<IReadOnlyList<MyTenantDto>>> GetMyTenants(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyTenantsQuery(), cancellationToken);
        return Ok(result);
    }

    /// <summary>The customer account's orders in every business, newest first: in progress, the ones before, or all (Fase 24C).</summary>
    [HttpGet("orders")]
    public async Task<ActionResult<PaginatedList<MyOrderDto>>> GetMyOrders(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] MyOrdersScope scope = MyOrdersScope.All,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetMyOrdersQuery { PageNumber = pageNumber, PageSize = pageSize, Scope = scope }, cancellationToken));

    /// <summary>One order of the account, step by step with the time of each one (Fase 24C); any other is not found.</summary>
    [HttpGet("orders/{id:guid}")]
    public async Task<ActionResult<MyOrderDetailDto>> GetMyOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await _mediator.Send(new GetMyOrderQuery(id), cancellationToken);
        return order is null ? this.ApiError(StatusCodes.Status404NotFound, "No encontramos ese pedido en tu cuenta.") : Ok(order);
    }

    /// <summary>The customer account's appointments booked online, in every business (Fase 24D): coming or before.</summary>
    [HttpGet("appointments")]
    public async Task<ActionResult<PaginatedList<CustomerAppointmentDto>>> GetMyAppointments(
        [FromQuery] MyAppointmentsScope scope = MyAppointmentsScope.Upcoming,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
        => Ok(await _mediator.Send(new GetMyAppointmentsQuery { Scope = scope, PageNumber = pageNumber, PageSize = pageSize }, cancellationToken));

    /// <summary>Cancels one of the account's appointments before it starts (Fase 24D); any other is not found.</summary>
    [HttpPost("appointments/{id:guid}/cancel")]
    public async Task<ActionResult<CustomerAppointmentDto>> CancelMyAppointment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelMyAppointmentCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The addresses the customer account keeps for its orders (Fase 24B), the last one used first.</summary>
    [HttpGet("addresses")]
    public async Task<ActionResult<List<AccountAddressDto>>> GetMyAddresses(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyAddressesQuery(), cancellationToken));

    /// <summary>Keeps an address in the customer account; the same place again takes the new name and references.</summary>
    [HttpPost("addresses")]
    public async Task<ActionResult<AccountAddressDto>> SaveMyAddress([FromBody] AccountAddressRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new SaveMyAddressCommand
        {
            Label = request.Label,
            PostalCode = request.PostalCode,
            AddressLine = request.AddressLine,
            References = request.References
        }, cancellationToken));

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<AccountAddressDto>> UpdateMyAddress(Guid id, [FromBody] AccountAddressRequest request, CancellationToken cancellationToken)
        => ToResult(await _mediator.Send(new UpdateMyAddressCommand
        {
            Id = id,
            Label = request.Label,
            PostalCode = request.PostalCode,
            AddressLine = request.AddressLine,
            References = request.References
        }, cancellationToken));

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteMyAddress(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteMyAddressCommand(id), cancellationToken);
        return result.IsFailure ? AddressError(result.Error!) : NoContent();
    }

    /// <summary>
    /// The businesses the customer account keeps among its favorites (Fase 24F), the last one kept first: each with its card
    /// in Explorar when it shows there, and with the delivery to the postal code given.
    /// </summary>
    [HttpGet("favorites")]
    public async Task<ActionResult<List<MyFavoriteBusinessDto>>> GetMyFavorites([FromQuery] string? postalCode, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyFavoritesQuery(postalCode), cancellationToken));

    /// <summary>Keeps a business among the account's favorites, by the slug of its store; keeping it again changes nothing.</summary>
    [HttpPut("favorites/{slug}")]
    public async Task<IActionResult> SaveMyFavorite(string slug, CancellationToken cancellationToken)
        => FavoriteResult(await _mediator.Send(new SaveMyFavoriteCommand(slug), cancellationToken));

    /// <summary>Takes a business out of the account's favorites; one that was not there changes nothing.</summary>
    [HttpDelete("favorites/{slug}")]
    public async Task<IActionResult> RemoveMyFavorite(string slug, CancellationToken cancellationToken)
        => FavoriteResult(await _mediator.Send(new RemoveMyFavoriteCommand(slug), cancellationToken));

    /// <summary>The reviews the customer account wrote (Fase 24H), the latest first, hidden ones too with why.</summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<List<MyReviewDto>>> GetMyReviews(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetMyReviewsQuery(), cancellationToken));

    /// <summary>Whether the account may review the business of a store, and the review it already wrote.</summary>
    [HttpGet("reviews/{slug}")]
    public async Task<ActionResult<MyReviewOfBusinessDto>> GetMyReview(string slug, CancellationToken cancellationToken)
    {
        var review = await _mediator.Send(new GetMyReviewOfBusinessQuery(slug), cancellationToken);
        return review is null ? this.ApiError(StatusCodes.Status404NotFound, ReviewRules.StoreNotFoundMessage) : Ok(review);
    }

    /// <summary>
    /// Writes the account's review of a business, or changes it: after an order received or an appointment attended with
    /// the account (403 before).
    /// </summary>
    [HttpPut("reviews/{slug}")]
    public async Task<ActionResult<MyReviewDto>> SaveMyReview(string slug, [FromBody] MyReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveMyReviewCommand(slug, request.Rating, request.Comment), cancellationToken);
        return result.IsFailure ? ReviewError(result.Error!) : Ok(result.Value);
    }

    /// <summary>Deletes the account's review of a business; none there changes nothing.</summary>
    [HttpDelete("reviews/{slug}")]
    public async Task<IActionResult> DeleteMyReview(string slug, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteMyReviewCommand(slug), cancellationToken);
        return result.IsFailure ? ReviewError(result.Error!) : NoContent();
    }

    /// <summary>Opens another business with this account, as its owner. Switch to it to finish its setup.</summary>
    [HttpPost("tenants")]
    public async Task<ActionResult<MyTenantDto>> CreateBusiness([FromBody] CreateBusinessCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    private ActionResult<AccountAddressDto> ToResult(Result<AccountAddressDto> result)
        => result.IsFailure ? AddressError(result.Error!) : Ok(result.Value);

    // A store that is not published is not found.
    private IActionResult FavoriteResult(Result result)
        => result.IsSuccess
            ? NoContent()
            : this.ApiError(result.Error == FavoriteBusinessRules.NotFoundMessage ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, result.Error!);

    // A store that is not published is not found; reviewing it before buying there is not allowed.
    private ObjectResult ReviewError(string error) => this.ApiError(error switch
    {
        ReviewRules.StoreNotFoundMessage => StatusCodes.Status404NotFound,
        ReviewRules.NotACustomerYetMessage => StatusCodes.Status403Forbidden,
        ReviewRules.WrittenMeanwhileMessage => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    }, error);

    // Another account's address, or one already deleted, is not found.
    private ObjectResult AddressError(string error)
        => this.ApiError(error == AccountAddressRules.NotFoundMessage ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest, error);
}

public record MyReviewRequest(int Rating, string? Comment = null);

public record AccountAddressRequest(string PostalCode, string AddressLine, string? Label = null, string? References = null);
