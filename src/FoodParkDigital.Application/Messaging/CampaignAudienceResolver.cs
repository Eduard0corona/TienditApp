using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

public sealed record CampaignRecipient(string Phone, string? Name);

/// <param name="ExcludedCount">Customers left out because they opted out.</param>
/// <param name="Truncated">More customers matched than <see cref="MessageCampaign.MaxRecipients"/>.</param>
public sealed record CampaignAudience(IReadOnlyList<CampaignRecipient> Recipients, int ExcludedCount, bool Truncated);

/// <summary>
/// Who a campaign reaches. Runs in the business's context, so the query filters keep it inside the business.
/// Phones are normalized and deduplicated by recipient key; opted-out customers are left out.
/// </summary>
public sealed class CampaignAudienceResolver
{
    private static readonly OrderStatus[] CompletedStatuses = [OrderStatus.Delivered, OrderStatus.Completed];
    /// <summary>
    /// Customers of Rappi, Uber Eats and DiDi Food belong to the platform: their orders keep no phone, and are left out
    /// here too, so a campaign never reaches them.
    /// </summary>
    private static readonly OrderChannel[] DeliveryPlatforms = [.. OrderChannelRules.DeliveryPlatforms];

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;

    public CampaignAudienceResolver(IApplicationDbContext context, IPhoneNormalizationService phones)
    {
        _context = context;
        _phones = phones;
    }

    public async Task<CampaignAudience> ResolveAsync(
        CampaignAudienceKind kind,
        int minimumOrders,
        int? days,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        MessageCampaign.ValidateAudience(kind, minimumOrders, days);
        var candidates = kind switch
        {
            CampaignAudienceKind.AllCustomers => await AllCustomersAsync(cancellationToken),
            CampaignAudienceKind.Buyers => await BuyersAsync(minimumOrders, days is { } since ? now.AddDays(-since) : null, cancellationToken),
            _ => await LapsedAsync(minimumOrders, now.AddDays(-days!.Value), cancellationToken)
        };

        var optedOut = (await _context.MessagingOptOuts
                .AsNoTracking()
                .Where(o => o.ResubscribedAt == null)
                .Select(o => o.Phone)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var recipients = new Dictionary<string, CampaignRecipient>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rawPhone, rawName) in candidates)
        {
            if (!_phones.TryNormalizeToE164(rawPhone, "MX", out var phone))
                continue;

            var key = MessagingPhone.ToKey(phone);
            if (optedOut.Contains(key))
            {
                excluded.Add(key);
                continue;
            }

            var name = string.IsNullOrWhiteSpace(rawName) ? null : rawName.Trim();
            if (!recipients.TryGetValue(key, out var current))
                recipients[key] = new CampaignRecipient(phone, name);
            else if (current.Name is null && name is not null)
                recipients[key] = current with { Name = name };
        }

        var ordered = recipients
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToList();
        var truncated = ordered.Count > MessageCampaign.MaxRecipients;
        return new CampaignAudience(
            truncated ? ordered.Take(MessageCampaign.MaxRecipients).ToList() : ordered,
            excluded.Count,
            truncated);
    }

    private async Task<List<(string Phone, string? Name)>> AllCustomersAsync(CancellationToken cancellationToken)
    {
        // Registered customers first: their names are the ones the business typed.
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.IsActive && !c.IsPublicGeneral && c.Phone != null && c.Phone != "")
            .Select(c => new { Phone = c.Phone!, c.Name })
            .ToListAsync(cancellationToken);
        var ordered = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerPhone != null && o.Status != OrderStatus.Cancelled && !DeliveryPlatforms.Contains(o.Channel))
            .GroupBy(o => o.CustomerPhone!)
            .Select(g => new { Phone = g.Key, Name = g.Max(o => o.CustomerName) })
            .ToListAsync(cancellationToken);

        return customers.Select(c => (c.Phone, (string?)c.Name))
            .Concat(ordered.Select(o => (o.Phone, (string?)o.Name)))
            .ToList();
    }

    private async Task<List<(string Phone, string? Name)>> BuyersAsync(
        int minimumOrders, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        var orders = _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerPhone != null && CompletedStatuses.Contains(o.Status) && !DeliveryPlatforms.Contains(o.Channel));
        if (since is { } from)
            orders = orders.Where(o => o.CreatedAt >= from);

        var buyers = await orders
            .GroupBy(o => o.CustomerPhone!)
            .Where(g => g.Count() >= minimumOrders)
            .Select(g => new { Phone = g.Key, Name = g.Max(o => o.CustomerName) })
            .ToListAsync(cancellationToken);
        return buyers.Select(b => (b.Phone, (string?)b.Name)).ToList();
    }

    private async Task<List<(string Phone, string? Name)>> LapsedAsync(
        int minimumOrders, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var lapsed = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerPhone != null && CompletedStatuses.Contains(o.Status) && !DeliveryPlatforms.Contains(o.Channel))
            .GroupBy(o => o.CustomerPhone!)
            .Where(g => g.Count() >= minimumOrders && g.Max(o => o.CreatedAt) < cutoff)
            .Select(g => new { Phone = g.Key, Name = g.Max(o => o.CustomerName) })
            .ToListAsync(cancellationToken);
        return lapsed.Select(l => (l.Phone, (string?)l.Name)).ToList();
    }
}
