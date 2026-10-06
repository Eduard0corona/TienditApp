using System.Collections.Concurrent;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// The codes anonymous requests ask for go out after the answer: the provider's time and failures stay out of it.
/// </summary>
public class VerificationCodeSenderTests
{
    private const string FailingPhone = "+525500000001";
    private const string FailingCode = "111111";
    private const string Phone = "+525512345678";
    private const string Code = "222222";

    [Fact]
    public async Task Sender_ShouldSendTheQueuedCodes_AndKeepGoingAfterAFailure()
    {
        var sms = new FlakySmsService(failFor: FailingPhone, expectedAttempts: 2);
        await using var services = new ServiceCollection()
            .AddScoped<IBackgroundExecutionContext, BackgroundExecutionContext>()
            .AddScoped<ISmsService>(_ => sms)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var queue = new VerificationCodeQueue(new CapturingLogger<VerificationCodeQueue>());
        var logger = new CapturingLogger<VerificationCodeSender>();
        using var sender = new VerificationCodeSender(queue, services.GetRequiredService<IServiceScopeFactory>(), logger);

        queue.Enqueue(FailingPhone, FailingCode);
        queue.Enqueue(Phone, Code);
        await sender.StartAsync(CancellationToken.None);
        await sms.AllAttempted.WaitAsync(TimeSpan.FromSeconds(10));
        // Stopping waits for the sends in flight, the failed one's log included.
        await sender.StopAsync(CancellationToken.None);

        Assert.Equal([(Phone, Code)], sms.Sent);
        var failure = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.NotNull(failure.Exception);
        Assert.False(failure.Mentions(FailingCode));
        Assert.False(failure.Mentions(FailingPhone));
    }

    [Fact]
    public void Queue_ShouldDropTheCodesPastItsCapacity_AndLogIt()
    {
        var logger = new CapturingLogger<VerificationCodeQueue>();
        var queue = new VerificationCodeQueue(logger);

        for (var i = 0; i <= VerificationCodeQueue.Capacity; i++)
            queue.Enqueue(Phone, Code);

        Assert.Equal(VerificationCodeQueue.Capacity, queue.Reader.Count);
        var dropped = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, dropped.Level);
        Assert.False(dropped.Mentions(Code));
        Assert.False(dropped.Mentions(Phone));
    }

    private sealed class FlakySmsService(string failFor, int expectedAttempts) : ISmsService
    {
        private readonly TaskCompletionSource _allAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attempts;

        public ConcurrentQueue<(string Phone, string Code)> Sent { get; } = new();
        public Task AllAttempted => _allAttempted.Task;

        public Task SendVerificationCodeAsync(string phoneNumber, string code)
        {
            if (Interlocked.Increment(ref _attempts) == expectedAttempts)
                _allAttempted.SetResult();
            if (phoneNumber == failFor)
                throw new HttpRequestException("The provider did not answer.");

            Sent.Enqueue((phoneNumber, code));
            return Task.CompletedTask;
        }

        public Task SendSmsAsync(string phoneNumber, string message) => Task.CompletedTask;
    }
}
