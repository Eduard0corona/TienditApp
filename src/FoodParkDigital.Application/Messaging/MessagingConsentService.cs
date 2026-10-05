using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Opt-outs from campaigns and reminders. It always runs in the scope of the business it is asked about (a staff
/// request, the messaging worker or the webhook, which set it), so the tenant filter applies; asking about another
/// business is a bug and fails instead of answering "not opted out".
/// </summary>
public sealed class MessagingConsentService
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IDateTimeProvider _clock;

    public MessagingConsentService(IApplicationDbContext context, ICurrentTenantService tenant, IDateTimeProvider clock)
    {
        _context = context;
        _tenant = tenant;
        _clock = clock;
    }

    /// <returns>True when the stored choice changed. The caller saves.</returns>
    public async Task<bool> SetAsync(
        Guid tenantId,
        string phone,
        bool optedOut,
        OptOutSource source,
        CancellationToken cancellationToken)
    {
        EnsureScope(tenantId);
        var key = MessagingPhone.ToKey(phone);
        var existing = await _context.MessagingOptOuts
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Phone == key, cancellationToken);
        var now = _clock.UtcNow;

        if (optedOut)
        {
            if (existing is null)
            {
                _context.MessagingOptOuts.Add(new MessagingOptOut(tenantId, key, source, now));
                return true;
            }

            if (existing.IsActive)
                return false;

            existing.OptOut(source, now);
            return true;
        }

        if (existing is not { IsActive: true })
            return false;

        existing.Resubscribe(now);
        return true;
    }

    public Task<bool> IsOptedOutAsync(Guid tenantId, string phone, CancellationToken cancellationToken)
    {
        EnsureScope(tenantId);
        var key = MessagingPhone.ToKey(phone);
        return _context.MessagingOptOuts
            .AnyAsync(o => o.TenantId == tenantId && o.Phone == key && o.ResubscribedAt == null, cancellationToken);
    }

    private void EnsureScope(Guid tenantId)
    {
        if (tenantId != _tenant.TenantId)
            throw new InvalidOperationException("Opt-outs are read and changed in the scope of their business.");
    }
}
