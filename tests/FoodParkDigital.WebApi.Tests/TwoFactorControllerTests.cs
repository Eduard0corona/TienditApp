using System.Security.Claims;
using System.Text.Json;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands.TwoFactor;
using FoodParkDigital.WebApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FoodParkDigital.WebApi.Tests;

public class TwoFactorControllerTests
{
    [Theory]
    [InlineData("""{ "code": "123 456" }""", "123 456", false)]
    [InlineData("""{ "code": "BCDFG-HJKMN", "useRecoveryCode": true }""", "BCDFG-HJKMN", true)]
    public async Task Disable2FA_ShouldSendBodyCodeForTokenUser(string body, string code, bool useRecoveryCode)
    {
        var userId = Guid.NewGuid();
        var mediator = new CapturingMediator(Result.Success());
        var request = JsonSerializer.Deserialize<Disable2FARequest>(body, JsonSerializerOptions.Web)!;

        var response = await CreateController(mediator, userId).Disable2FA(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal(new Disable2FACommand(userId, code, useRecoveryCode), mediator.Request);
    }

    [Fact]
    public async Task Disable2FA_ShouldAnswerHandlerErrorAsBadRequest()
    {
        var mediator = new CapturingMediator(Result.Failure(Disable2FACommandHandler.InvalidCodeError));

        var response = await CreateController(mediator, Guid.NewGuid())
            .Disable2FA(new Disable2FARequest("000000"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        var error = JsonSerializer.SerializeToElement(badRequest.Value, JsonSerializerOptions.Web).GetProperty("error");
        Assert.Equal(Disable2FACommandHandler.InvalidCodeError, error.GetString());
    }

    private static TwoFactorController CreateController(IMediator mediator, Guid userId) =>
        new(mediator, new TestWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
                }
            }
        };

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "FoodParkDigital.WebApi";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly Result _result;

        public CapturingMediator(Result result)
        {
            _result = result;
        }

        public object? Request { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult((TResponse)(object)_result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}
