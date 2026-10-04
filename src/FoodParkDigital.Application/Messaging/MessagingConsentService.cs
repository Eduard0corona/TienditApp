using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Opt-outs from campaigns and reminders. The webhook runs without a tenant context, so the business is always
/// passed in and filtered explicitly (the same query serves the staff screen, where it is the current tenant).
/// </summary>
public sealed class MessagingConsentService
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public MessagingConsentService(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
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
        var key = MessagingPhone.ToKey(phone);
        var existing = await _context.MessagingOptOuts
            .IgnoreQueryFilters()
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
        var key = MessagingPhone.ToKey(phone);
        return _context.MessagingOptOuts
            .IgnoreQueryFilters()
            .AnyAsync(o => o.TenantId == tenantId && o.Phone == key && o.ResubscribedAt == null, cancellationToken);
    }
}
