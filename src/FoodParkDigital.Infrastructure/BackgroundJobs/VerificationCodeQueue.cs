using System.Threading.Channels;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// The verification codes anonymous requests asked for, sent by <see cref="VerificationCodeSender"/> after the answer.
/// In memory: a code still queued when the app stops is lost, and the person asks for another one after the one-minute
/// cooldown.
/// </summary>
public sealed class VerificationCodeQueue : IVerificationCodeQueue
{
    // Bounded: a flood of requests cannot take the memory. Past it a code is dropped, never waited for.
    internal const int Capacity = 1000;

    private readonly Channel<VerificationCode> _channel = Channel.CreateBounded<VerificationCode>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait });
    private readonly ILogger<VerificationCodeQueue> _logger;

    public VerificationCodeQueue(ILogger<VerificationCodeQueue> logger)
    {
        _logger = logger;
    }

    internal ChannelReader<VerificationCode> Reader => _channel.Reader;

    public void Enqueue(string phoneNumber, string code)
    {
        // With FullMode.Wait a write to a full queue fails instead of dropping a code silently.
        if (!_channel.Writer.TryWrite(new VerificationCode(phoneNumber, code)))
            _logger.LogWarning("The verification code queue is full; a code was not sent.");
    }
}

internal sealed record VerificationCode(string PhoneNumber, string Code);
