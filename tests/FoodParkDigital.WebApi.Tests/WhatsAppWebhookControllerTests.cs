using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Application.WhatsApp.Queries;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.WhatsApp.Entities;
using FoodParkDigital.Domain.WhatsApp.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Controllers;
using FoodParkDigital.WebApi.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.WebApi.Tests.WhatsAppWebhookTestSupport;

namespace FoodParkDigital.WebApi.Tests;

public class WhatsAppWebhookControllerTests
{
    private const string TenantAPhoneNumberId = "111111111111111";
    private const string TenantBPhoneNumberId = "222222222222222";
    private const string TenantAVerifyToken = "a1a1a1a1a1a1a1a1a1a1a1a1";
    private const string TenantBVerifyToken = "b2b2b2b2b2b2b2b2b2b2b2b2";

    [Fact]
    public async Task Receive_ShouldReturn401AndNotProcess_WhenSignatureIsInvalid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.forged");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, "attacker-secret"));

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldReturn401AndNotProcess_WhenSignatureIsMissing()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.unsigned");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, signature: null);

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldReturn503AndNotProcess_WhenAppSecretIsNotConfigured()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.no-secret");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, appSecret: null), body, Sign(body, "any-secret"));

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldNotProcess_WhenUnsignedWebhooksFlagIsSetOutsideDevelopment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.flag-in-production");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, appSecret: null, allowUnsignedWebhooks: "true"),
            body,
            signature: null);

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldRouteMessageToTenantOwningPhoneNumberId_WhenSignatureIsValid()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.valid");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        var command = Assert.IsType<ProcessInboundMessageCommand>(Assert.Single(fixture.Mediator.Requests));
        Assert.Equal(fixture.TenantB, command.TenantId);
        Assert.Equal("wamid.valid", command.ExternalMessageId);
        Assert.Equal("+5215511112222", command.FromPhone);
        Assert.Equal("Hola", command.Body);
    }

    [Fact]
    public async Task Receive_ShouldProcessUnsignedDelivery_WhenUnsignedWebhooksFlagIsSetInDevelopment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload(TenantAPhoneNumberId, "wamid.local-dev");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Development, appSecret: null, allowUnsignedWebhooks: "true"),
            body,
            signature: null);

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        var command = Assert.IsType<ProcessInboundMessageCommand>(Assert.Single(fixture.Mediator.Requests));
        Assert.Equal(fixture.TenantA, command.TenantId);
    }

    [Fact]
    public async Task Receive_ShouldProcessRemainingMessagesAndReturn500_WhenAMessageCannotBeStored()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.Mediator.FailingMessageIds.Add("wamid.2");
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.1", "wamid.2", "wamid.3");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        // Meta redelivers anything that does not get a 200; the stored messages are then deduplicated.
        Assert.Equal(StatusCodes.Status500InternalServerError, StatusCodeOf(result));
        Assert.Equal(
            new[] { "wamid.1", "wamid.2", "wamid.3" },
            fixture.Mediator.Requests.OfType<ProcessInboundMessageCommand>().Select(c => c.ExternalMessageId));
    }

    [Fact]
    public async Task Receive_ShouldReturn200_WhenRedeliveryRepeatsAlreadyStoredMessages()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.Mediator.DuplicateMessageIds.Add("wamid.1");
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.1", "wamid.2");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Equal(2, fixture.Mediator.Requests.Count);
    }

    [Fact]
    public async Task Receive_ShouldReturn200AndNotProcess_WhenSignedPayloadIsNotValidJson()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = Encoding.UTF8.GetBytes("""{ "entry": [""");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        // A redelivery would carry the same body, so it is acknowledged instead of retried.
        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldReturn200AndNotProcess_WhenPhoneNumberIdIsUnknown()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var body = InboundPayload("999999999999999", "wamid.unknown-number");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCodeOf(result));
        Assert.Empty(fixture.Mediator.Requests);
    }

    [Fact]
    public async Task Receive_ShouldReturn500_WhenStatusUpdateCannotBeSaved()
    {
        await using var fixture = await TestFixture.CreateAsync();
        await fixture.SeedSentMessageAsync(fixture.TenantB, "wamid.sent");
        fixture.SaveFaults.FailNextSave();
        var body = StatusPayload(TenantBPhoneNumberId, "wamid.sent", "delivered");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret));

        var result = await controller.Receive(CancellationToken.None);

        Assert.True(fixture.SaveFaults.Fired);
        Assert.Equal(StatusCodes.Status500InternalServerError, StatusCodeOf(result));
    }

    [Fact]
    public async Task Receive_ShouldNotLogSenderPhone_WhenStoringMessageFailsWithSqlErrorOutsideDevelopment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        fixture.Mediator.ProcessFailure = DuplicateConversationFailure(fixture.TenantB);
        var logger = new CapturingLogger<WhatsAppWebhookController>();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.race");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Production, AppSecret), body, Sign(body, AppSecret), logger);

        var result = await controller.Receive(CancellationToken.None);

        // The message was not stored, so Meta must deliver it again.
        Assert.Equal(StatusCodes.Status500InternalServerError, StatusCodeOf(result));
        Assert.Null(logger.FirstMentioning("5511112222"));
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains($"{typeof(SqlException).FullName} (SQL error 2601)", error.Exception!.ToString());
    }

    [Fact]
    public async Task Receive_ShouldLogFullSqlError_WhenStoringMessageFailsInDevelopment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var failure = DuplicateConversationFailure(fixture.TenantB);
        fixture.Mediator.ProcessFailure = failure;
        var logger = new CapturingLogger<WhatsAppWebhookController>();
        var body = InboundPayload(TenantBPhoneNumberId, "wamid.race-dev");
        var controller = fixture.CreateController(
            CreateVerifier(Environments.Development, AppSecret), body, Sign(body, AppSecret), logger, Environments.Development);

        await controller.Receive(CancellationToken.None);

        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, error.Exception);
    }

    [Fact]
    public async Task Verify_ShouldReturnChallenge_WhenVerifyTokenMatches()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = fixture.CreateController(CreateVerifier(Environments.Production, AppSecret));

        var result = await controller.Verify("subscribe", TenantBVerifyToken, "challenge-123", CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("challenge-123", content.Content);
    }

    [Fact]
    public async Task Verify_ShouldReturn401_WhenVerifyTokenIsUnknown()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var controller = fixture.CreateController(CreateVerifier(Environments.Production, AppSecret));

        var result = await controller.Verify("subscribe", "unknown-token", "challenge-123", CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
    }

    private static int? StatusCodeOf(IActionResult result)
        => Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode;

    // SQL Server quotes row values in its errors, such as the sender's phone in this unique-index violation.
    private static DbUpdateException DuplicateConversationFailure(Guid tenantId)
        => SqlExceptionFactory.SaveFailure(
            2601,
            "Cannot insert duplicate key row in object 'dbo.WhatsAppConversations' with unique index "
            + "'IX_WhatsAppConversations_TenantId_CustomerPhone'. The duplicate key value is "
            + $"({tenantId}, +5215511112222).");

    private static byte[] InboundPayload(string phoneNumberId, params string[] messageIds)
    {
        var messages = string.Join(",", messageIds.Select(messageId => $$"""
            {
              "from": "5215511112222",
              "id": "{{messageId}}",
              "timestamp": "1759300000",
              "type": "text",
              "text": { "body": "Hola" }
            }
            """));

        return WebhookPayload(phoneNumberId, $$"""
            "contacts": [{ "profile": { "name": "Cliente" }, "wa_id": "5215511112222" }],
            "messages": [{{messages}}]
            """);
    }

    private static byte[] StatusPayload(string phoneNumberId, string messageId, string status)
        => WebhookPayload(phoneNumberId, $$"""
            "statuses": [{
              "id": "{{messageId}}",
              "status": "{{status}}",
              "timestamp": "1759300001",
              "recipient_id": "5215511112222"
            }]
            """);

    private static byte[] WebhookPayload(string phoneNumberId, string valueFields) => Encoding.UTF8.GetBytes($$"""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "id": "test-waba-id",
            "changes": [{
              "field": "messages",
              "value": {
                "messaging_product": "whatsapp",
                "metadata": { "display_phone_number": "5215500000000", "phone_number_id": "{{phoneNumberId}}" },
                {{valueFields}}
              }
            }]
          }]
        }
        """);

    private sealed class TestFixture : IAsyncDisposable
    {
        private TestFixture(ApplicationDbContext context, Guid tenantA, Guid tenantB, SaveChangesFaultInterceptor saveFaults)
        {
            Context = context;
            TenantA = tenantA;
            TenantB = tenantB;
            SaveFaults = saveFaults;
            Mediator = new RecordingMediator(context);
        }

        public ApplicationDbContext Context { get; }
        public Guid TenantA { get; }
        public Guid TenantB { get; }
        public SaveChangesFaultInterceptor SaveFaults { get; }
        public RecordingMediator Mediator { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var saveFaults = new SaveChangesFaultInterceptor();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(saveFaults)
                .Options;

            var context = new ApplicationDbContext(options, new TestCurrentTenantService(), new NullMediator());

            var tenantA = new Tenant("Tenant A", "tenant-a", BusinessType.Food, "a@tenant.com");
            var tenantB = new Tenant("Tenant B", "tenant-b", BusinessType.Retail, "b@tenant.com");
            context.Tenants.AddRange(tenantA, tenantB);
            context.WhatsAppConfigs.AddRange(
                CreateConfig(tenantA.Id, TenantAPhoneNumberId, TenantAVerifyToken),
                CreateConfig(tenantB.Id, TenantBPhoneNumberId, TenantBVerifyToken));
            await context.SaveChangesAsync();

            return new TestFixture(context, tenantA.Id, tenantB.Id, saveFaults);
        }

        /// <summary>Stores a message the business sent, whose delivery statuses Meta reports later.</summary>
        public async Task SeedSentMessageAsync(Guid tenantId, string externalMessageId)
        {
            var conversation = new WhatsAppConversation(tenantId, "+5215511112222", "Cliente");
            conversation.AddMessage(MessageDirection.Outbound, "Tu pedido va en camino", externalMessageId);
            Context.WhatsAppConversations.Add(conversation);
            await Context.SaveChangesAsync();
        }

        public WhatsAppWebhookController CreateController(
            WhatsAppWebhookSignatureVerifier verifier,
            byte[]? body = null,
            string? signature = null,
            ILogger<WhatsAppWebhookController>? logger = null,
            string environmentName = "Production")
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(body ?? []);
            if (signature is not null)
                httpContext.Request.Headers[WhatsAppWebhookSignatureVerifier.SignatureHeaderName] = signature;

            return new WhatsAppWebhookController(
                Mediator,
                Context,
                verifier,
                new TestHostEnvironment(environmentName),
                logger ?? NullLogger<WhatsAppWebhookController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();

        private static WhatsAppConfig CreateConfig(Guid tenantId, string phoneNumberId, string verifyToken)
        {
            var config = new WhatsAppConfig(tenantId);
            config.Update(true, "test-access-token", phoneNumberId, "test-waba-id", null, verifyToken);
            return config;
        }
    }

    /// <summary>Records every request; resolves verify-token queries with the real handler.</summary>
    private sealed class RecordingMediator : IMediator
    {
        private readonly ApplicationDbContext _context;

        public RecordingMediator(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<object> Requests { get; } = [];

        /// <summary>When set, processing an inbound message throws it, like a failed save would.</summary>
        public Exception? ProcessFailure { get; set; }

        /// <summary>Inbound messages whose processing throws, as a database outage would.</summary>
        public HashSet<string> FailingMessageIds { get; } = [];

        /// <summary>Inbound messages the handler reports as already stored by an earlier delivery.</summary>
        public HashSet<string> DuplicateMessageIds { get; } = [];

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Requests.Add(request);
            object response = request switch
            {
                VerifyWhatsAppWebhookTokenQuery query =>
                    await new VerifyWhatsAppWebhookTokenQueryHandler(_context).Handle(query, ct),
                ProcessInboundMessageCommand when ProcessFailure is not null => throw ProcessFailure,
                ProcessInboundMessageCommand command when FailingMessageIds.Contains(command.ExternalMessageId) =>
                    throw new RetryLimitExceededException("Simulated database outage.", new TimeoutException()),
                ProcessInboundMessageCommand command when DuplicateMessageIds.Contains(command.ExternalMessageId) =>
                    Result.Failure<ChatMessageDto>(ProcessInboundMessageCommandHandler.DuplicateMessageError),
                ProcessInboundMessageCommand => Result.Success(new ChatMessageDto()),
                _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
            };

            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Fails the next SaveChanges, as a database outage would.</summary>
    private sealed class SaveChangesFaultInterceptor : SaveChangesInterceptor
    {
        private bool _armed;

        public bool Fired { get; private set; }

        public void FailNextSave() => _armed = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!_armed)
                return ValueTask.FromResult(result);

            _armed = false;
            Fired = true;
            throw new DbUpdateException("Simulated database outage.");
        }
    }

    private sealed class TestCurrentTenantService : ICurrentTenantService
    {
        public Guid TenantId { get; private set; }
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class NullMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
            => Task.FromResult<TResponse>(default!);

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task Publish(object notification, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default)
            => AsyncEnumerable.Empty<object?>();

        public Task<object?> Send(object request, CancellationToken ct = default)
            => Task.FromResult<object?>(null);
    }
}
