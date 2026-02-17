namespace FoodParkDigital.Application.Common.Interfaces;

public interface IAiAgentAutoReplyService
{
    Task<string?> BuildReplyAsync(
        Guid tenantId,
        string incomingMessage,
        string? customerName,
        CancellationToken cancellationToken = default);
}
