using FoodParkDigital.Application.Appointments.OnlineBooking;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.WebApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The endpoints of «Quién atiende» (Fase 31I, ADR 0089): the days a person does not attend reach the command as sent (none
/// keeps them), and the photo goes to its command with the person of the route; without a file nothing is sent.
/// </summary>
public class OnlineBookingStaffEndpointsTests
{
    [Fact]
    public async Task Update_ShouldPassTheAbsences_AndNoneWhenTheyAreNotSent()
    {
        var mediator = new CapturingMediator();
        var controller = new AppointmentsController(mediator);
        var userId = Guid.NewGuid();
        var absences = new List<StaffAbsenceDto> { new() { From = new DateOnly(2026, 11, 16), To = new DateOnly(2026, 11, 18) } };

        await controller.UpdateOnlineBookingStaff(
            userId, new UpdateOnlineBookingStaffRequest { Enabled = true, Name = "Ana L.", Absences = absences }, CancellationToken.None);
        await controller.UpdateOnlineBookingStaff(
            userId, new UpdateOnlineBookingStaffRequest { Enabled = true, Name = "Ana L." }, CancellationToken.None);

        var sent = mediator.Requests.Cast<UpdateOnlineBookingStaffCommand>().ToList();
        Assert.Equal(userId, sent[0].UserId);
        Assert.Same(absences, sent[0].Absences);
        Assert.Null(sent[1].Absences);
    }

    [Fact]
    public async Task Photo_ShouldGoWithThePersonOfTheRoute_AndNothingWithoutAFile()
    {
        var mediator = new CapturingMediator();
        var controller = new AppointmentsController(mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var userId = Guid.NewGuid();
        await using var content = new MemoryStream([1, 2, 3]);
        var file = new FormFile(content, 0, content.Length, "file", "ana.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };

        var empty = await controller.UploadOnlineBookingStaffPhoto(
            userId, new FormFile(Stream.Null, 0, 0, "file", "vacio.png"), CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<ObjectResult>(empty.Result).StatusCode);
        Assert.Empty(mediator.Requests);

        await controller.UploadOnlineBookingStaffPhoto(userId, file, CancellationToken.None);
        await controller.DeleteOnlineBookingStaffPhoto(userId, CancellationToken.None);

        var upload = Assert.IsType<UploadOnlineBookingStaffPhotoCommand>(mediator.Requests[0]);
        Assert.Equal((userId, "ana.png", "image/png", 3L), (upload.UserId, upload.FileName, upload.ContentType, upload.FileSize));
        Assert.Equal(userId, Assert.IsType<DeleteOnlineBookingStaffPhotoCommand>(mediator.Requests[1]).UserId);
    }

    private sealed class CapturingMediator : IMediator
    {
        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Requests.Add(request);
            object answer = request switch
            {
                UpdateOnlineBookingStaffCommand => Result.Success(new OnlineBookingStaffMemberDto()),
                _ => Result.Success(new OnlineBookingStaffPhotoDto())
            };
            return Task.FromResult((TResponse)answer);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken ct = default) => Task.FromResult<object?>(null);
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}
