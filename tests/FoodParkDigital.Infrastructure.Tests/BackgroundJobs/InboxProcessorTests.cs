using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

public class InboxProcessorTests
{
    private const string PhoneNumberId = "123456789012345";

    [Fact]
    public async Task StoredDelivery_ShouldBeProcessedInTheBackground()
    {
        var sender = new ProgrammableSender();
        await using var host = await CreateHostAsync(sender);
        await EnqueueAsync(host, Delivery("wamid.1", "wamid.2"));

        Assert.Equal(1, await Processor(host).ProcessDueAsync(CancellationToken.None));

        Assert.Equal(["wamid.1", "wamid.2"], sender.Processed);
        var stored = Assert.Single(await InboxAsync(host));
        Assert.NotNull(stored.ProcessedAt);
        Assert.Equal(0, stored.Attempts);
    }

    [Fact]
    public async Task DeliveryWithAFailedMessage_ShouldBeRetriedLater()
    {
        var sender = new ProgrammableSender();
        sender.Failing.Add("wamid.2");
        await using var host = await CreateHostAsync(sender);
        await EnqueueAsync(host, Delivery("wamid.1", "wamid.2"));

        await Processor(host).ProcessDueAsync(CancellationToken.None);

        var failed = Assert.Single(await InboxAsync(host));
        Assert.Null(failed.ProcessedAt);
        Assert.Equal(1, failed.Attempts);
        Assert.Equal(host.Clock.UtcNow.Add(RetrySchedule.DelayAfter(1)), failed.NextAttemptAt);

        // The outage is over: the retry stores the missing message (wamid.1 is deduplicated by the handler).
        sender.Failing.Clear();
        host.Clock.Advance(RetrySchedule.DelayAfter(1));
        await Processor(host).ProcessDueAsync(CancellationToken.None);

        Assert.Equal(["wamid.1", "wamid.2", "wamid.1", "wamid.2"], sender.Processed);
        Assert.NotNull(Assert.Single(await InboxAsync(host)).ProcessedAt);
    }

    [Fact]
    public async Task PayloadThatIsNotJson_ShouldBeMarkedProcessedWithoutRetries()
    {
        var sender = new ProgrammableSender();
        await using var host = await CreateHostAsync(sender);
        await EnqueueAsync(host, """{ "entry": [""");

        await Processor(host).ProcessDueAsync(CancellationToken.None);

        Assert.Empty(sender.Processed);
        Assert.NotNull(Assert.Single(await InboxAsync(host)).ProcessedAt);
    }

    [Fact]
    public async Task Cleanup_ShouldDeleteProcessedDeliveries_AfterTheRetention()
    {
        await using var host = await CreateHostAsync(new ProgrammableSender());
        await EnqueueAsync(host, Delivery("wamid.1"));
        await Processor(host).ProcessDueAsync(CancellationToken.None);

        host.Clock.Advance(TimeSpan.FromDays(8));

        Assert.Equal(1, await Processor(host).DeleteProcessedAsync(CancellationToken.None));
        Assert.Empty(await InboxAsync(host));
    }

    private static InboxProcessor Processor(OutboxTestHost host) => host.Get<InboxProcessor>();

    private static async Task<OutboxTestHost> CreateHostAsync(ProgrammableSender sender)
    {
        var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
            services.AddSingleton<ISender>(sender);
            services.AddSingleton<InboxSignal>();
            services.AddScoped<IWebhookInbox, WebhookInbox>();
            services.AddScoped<WhatsAppWebhookProcessor>();
            services.AddSingleton<InboxProcessor>();
        });

        await host.InScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            var tenant = new Tenant("Taquería", "taqueria", FoodParkDigital.Domain.Tenants.Enums.BusinessType.Food, "t@t.mx");
            var config = new WhatsAppConfig(tenant.Id);
            config.Update(true, "token", PhoneNumberId, "waba", null, "verify-token-123456789012");
            context.Tenants.Add(tenant);
            context.WhatsAppConfigs.Add(config);
            await context.SaveChangesAsync();
            return true;
        });
        return host;
    }

    private static Task<bool> EnqueueAsync(OutboxTestHost host, string payload)
        => host.InScopeAsync(services => services.GetRequiredService<IWebhookInbox>().EnqueueAsync(WebhookSources.WhatsApp, payload));

    private static Task<List<InboxMessage>> InboxAsync(OutboxTestHost host)
        => host.InScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().InboxMessages.AsNoTracking().ToListAsync());

    private static string Delivery(params string[] messageIds)
    {
        var messages = string.Join(",", messageIds.Select(id => $$"""
            { "from": "5215511112222", "id": "{{id}}", "type": "text", "text": { "body": "Hola" } }
            """));
        return $$"""
            {
              "entry": [{
                "changes": [{
                  "value": {
                    "metadata": { "phone_number_id": "{{PhoneNumberId}}" },
                    "messages": [{{messages}}]
                  }
                }]
              }]
            }
            """;
    }

    private sealed class ProgrammableSender : ISender
    {
        public List<string> Processed { get; } = [];
        public HashSet<string> Failing { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ProcessInboundMessageCommand command)
            {
                Processed.Add(command.ExternalMessageId);
                if (Failing.Contains(command.ExternalMessageId))
                    throw new TimeoutException("Simulated database outage.");
            }

            return Task.FromResult<TResponse>(default!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<object?>();
    }
}
