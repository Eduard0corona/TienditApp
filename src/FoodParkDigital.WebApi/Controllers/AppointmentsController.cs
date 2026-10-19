using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Appointments.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Appointments.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FoodParkDigital.WebApi.Errors;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppointmentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedList<AppointmentDto>>> GetAll(
        [FromQuery] AppointmentStatus? status,
        [FromQuery] DateTimeOffset? fromDate,
        [FromQuery] DateTimeOffset? toDate,
        [FromQuery] string? search,
        [FromQuery] bool? homeVisit,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetAppointmentsQuery
        {
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
            HomeVisit = homeVisit,
            PageNumber = pageNumber,
            PageSize = pageSize
        }, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The visits at the customers' addresses of a day (in the business's time zone), by team member and in the order of
    /// their time (Fase 22E); optionally of one team member.
    /// </summary>
    [HttpGet("route")]
    public async Task<ActionResult<VisitRouteDto>> GetRoute(
        [FromQuery] DateOnly date,
        [FromQuery] Guid? staffUserId,
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetVisitRouteQuery(date, staffUserId), cancellationToken));

    /// <summary>The zone and the charge of a visit to a postal code, while the appointment is written (Fase 22E).</summary>
    [HttpGet("visit-quote")]
    public async Task<ActionResult<VisitQuoteDto>> GetVisitQuote([FromQuery] string postalCode, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetVisitQuoteQuery(postalCode), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Whether customers book appointments online, how many the business attends at once and what is missing (Fase 24D).</summary>
    [HttpGet("online-booking")]
    public async Task<ActionResult<OnlineBookingSettingsDto>> GetOnlineBooking(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetOnlineBookingSettingsQuery(), cancellationToken));

    /// <summary>Turns online appointments on or off and sets how many the business attends at once (owners and admins).</summary>
    [HttpPut("online-booking")]
    public async Task<ActionResult<OnlineBookingSettingsDto>> UpdateOnlineBooking(
        [FromBody] UpdateOnlineBookingSettingsCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>The business's active team, with who customers book online and their services and hours (Fase 24J).</summary>
    [HttpGet("online-booking/staff")]
    public async Task<ActionResult<OnlineBookingStaffSettingsDto>> GetOnlineBookingStaff(CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetOnlineBookingStaffQuery(), cancellationToken));

    /// <summary>Sets what customers see of a team member and when they book them online (owners and admins, Fase 24J).</summary>
    [HttpPut("online-booking/staff/{userId:guid}")]
    public async Task<ActionResult<OnlineBookingStaffMemberDto>> UpdateOnlineBookingStaff(
        Guid userId, [FromBody] UpdateOnlineBookingStaffRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateOnlineBookingStaffCommand
        {
            UserId = userId,
            Enabled = request.Enabled,
            Name = request.Name,
            AllServices = request.AllServices,
            ServiceIds = request.ServiceIds,
            WeeklyHours = request.WeeklyHours
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Accepts an appointment a customer requested online (Fase 24D): it is agendada and the customer hears it.</summary>
    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<AppointmentDto>> Accept(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AcceptAppointmentRequestCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    /// <summary>Declines an appointment a customer requested online (Fase 24D): its time is free and the customer hears it.</summary>
    [HttpPost("{id:guid}/decline")]
    public async Task<ActionResult<AppointmentDto>> Decline(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeclineAppointmentRequestCommand(id), cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAppointmentByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<AppointmentDto>> Create([FromBody] CreateAppointmentCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>Edits an open appointment, including moving it to another time.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AppointmentDto>> Update(Guid id, [FromBody] UpdateAppointmentCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command with { AppointmentId = id }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<AppointmentDto>> UpdateStatus(Guid id, [FromBody] UpdateAppointmentStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateAppointmentStatusCommand
        {
            AppointmentId = id,
            NewStatus = request.NewStatus
        }, cancellationToken);
        if (result.IsFailure)
            return this.ApiError(StatusCodes.Status400BadRequest, result.Error);
        return Ok(result.Value);
    }
}

public class UpdateAppointmentStatusRequest
{
    public AppointmentStatus NewStatus { get; init; }
}

public class UpdateOnlineBookingStaffRequest
{
    public bool Enabled { get; init; }
    public string Name { get; init; } = null!;
    public bool AllServices { get; init; } = true;
    public List<Guid>? ServiceIds { get; init; }
    public List<FoodParkDigital.Application.Tenants.DTOs.OpeningIntervalDto>? WeeklyHours { get; init; }
}
