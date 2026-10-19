using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.OnlinePayments;

/// <summary>
/// Reads a Mercado Pago notification stored in the inbox (Fase 24I, ADR 0031): it finds the businesses that connected the
/// account it names (the one read across businesses; an account may be connected by several businesses of one owner) and
/// applies the payment in each business's own scope. The payment is read from Mercado Pago there, with that business's key,
/// and only the business whose checkout it is applies it.
/// </summary>
public sealed class MercadoPagoWebhookProcessor
{
    private const string WebhookActor = "mercadopago-webhook";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<MercadoPagoWebhookProcessor> _logger;

    public MercadoPagoWebhookProcessor(
        IServiceScopeFactory scopeFactory,
        IApplicationDbContext context,
        ILogger<MercadoPagoWebhookProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _context = context;
        _logger = logger;
    }

    /// <returns>Why it must be retried (Mercado Pago did not answer), or null.</returns>
    public async Task<string?> ProcessAsync(string payload, CancellationToken cancellationToken)
    {
        var (paymentId, userId) = Read(payload);
        if (paymentId is null || userId is not > 0)
            return null;

        // The notification is the entry point that discovers the business: its account crosses the tenant filter on
        // purpose, and only to know which businesses to try.
        var tenants = await _context.OnlinePaymentAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.ProviderUserId == userId)
            .Select(a => a.TenantId)
            .ToListAsync(cancellationToken);
        if (tenants.Count == 0)
        {
            _logger.LogInformation("Mercado Pago notified a payment of an account no business has connected");
            return null;
        }

        var failed = 0;
        foreach (var tenantId in tenants)
        {
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WebhookActor);
                var outcome = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ApplyOnlinePaymentNotificationCommand(paymentId), cancellationToken);
                if (outcome.IsSuccess && outcome.Value != OnlinePaymentApplication.Ignored)
                    _logger.LogInformation("Mercado Pago payment applied for tenant {TenantId}: {Outcome}", tenantId, outcome.Value);
            }
            catch (OnlinePaymentGatewayUnavailableException)
            {
                failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One business failing does not leave the others without the payment: the delivery is retried.
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "The Mercado Pago notification could not be applied for tenant {TenantId}", tenantId);
                failed++;
            }
        }

        return failed > 0 ? "The notified payment could not be applied for every business; it is retried." : null;
    }

    private static (string? PaymentId, long? UserId) Read(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var paymentId = root.TryGetProperty("paymentId", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            long? userId = root.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.Number && u.TryGetInt64(out var id)
                ? id
                : null;
            return (paymentId, userId);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
