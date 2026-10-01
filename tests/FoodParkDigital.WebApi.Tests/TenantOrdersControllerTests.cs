using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Controllers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Tests;

public class TenantOrdersControllerTests
{
    [Fact]
    public async Task Create_ShouldForbid_WhenRouteTenantDiffersFromRequestTenant()
    {
        var requestTenant = Guid.NewGuid();
        var routeTenant = Guid.NewGuid();
        var mediator = new RecordingMediator();
        var tenantService = new FakeCurrentTenantService();
        tenantService.SetTenant(requestTenant);

        var controller = new TenantOrdersController(mediator, new AlwaysMemberAccess(), tenantService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"))
                }
            }
        };

        var response = await controller.Create(routeTenant, new CreateTenantOrderRequest { Phone = "+525511112222" });

        Assert.IsType<ForbidResult>(response.Result);
        Assert.Equal(0, mediator.Calls);
    }

    private sealed class FakeCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class AlwaysMemberAccess : ITenantAccessService
    {
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantAccessSnapshot(true, TenantCapability.Retail, []));
        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class RecordingMediator : IMediator
    {
        public int Calls { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<TResponse>(default!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
        {
            Calls++;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<object?>(null);
        }

        public Task Publish(object notification, CancellationToken ct = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default) where TNotification : INotification => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => AsyncEnumerable.Empty<object?>();
    }
}
