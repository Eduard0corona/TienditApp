using System.Globalization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Queues the WhatsApp notice of what comes due soon (Fase 26E, ADR 0043): to each customer (by phone) who is up to date and
/// owes something whose last day to pay, by their term, is within the next days, once per that last day to pay and at most
/// one a day. Never the day they started to owe it, never to who has something overdue (that is the overdue reminder's),
/// never to who wrote BAJA, only during the business's day and while WhatsApp is connected. Runs in the business's context;
/// the caller saves. The key includes the last day to pay and the business day, so two passes (or two instances) never
/// queue the same notice twice.
/// </summary>
public sealed class CreditDueSoonPlanner
{
    public const int MaxPerPass = CreditReminderPlanner.MaxPerPass;
    private const string KeyPrefix = "creditsoon:";

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public CreditDueSoonPlanner(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    /// <summary>Accounts queued per pass and business, at most: the rest go in the next passes.</summary>
    internal int PassLimit { get; init; } = MaxPerPass;

    public static string Key(string phoneKey, DateOnly dueOn, DateOnly businessDay)
        => $"{KeyPrefix}{phoneKey}:{dueOn:yyyyMMdd}:{businessDay:yyyyMMdd}";

    /// <summary>The phone key, the last day to pay and the business day of a notice's key.</summary>
    public static bool TryParseKey(string? key, out string phone, out DateOnly dueOn, out DateOnly day)
    {
        phone = string.Empty;
        dueOn = default;
        day = default;
        if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            return false;
        var parts = key[KeyPrefix.Length..].Split(':');
        if (parts.Length != 3 || parts[0].Length == 0)
            return false;
        phone = parts[0];
        return DateOnly.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dueOn)
            && DateOnly.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    /// <returns>Notices queued.</returns>
    public async Task<int> QueueDueAsync(TenantSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.CreditDueSoonEnabled)
            return 0;

        var now = _clock.UtcNow;
        var zone = TenantDateTimeResolver.Resolve(settings.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var timeOfDay = TimeOnly.FromDateTime(local.DateTime);
        if (timeOfDay < CreditReminderPlanner.QueueFrom || timeOfDay >= CreditReminderPlanner.QueueUntil)
            return 0;
        // A notice without WhatsApp would only fail: none is queued until it is connected again.
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, settings.TenantId, cancellationToken))
            return 0;

        var today = DateOnly.FromDateTime(local.Date);
        var startOfToday = TenantDateTimeResolver.StartOfLocalDayUtc(local.Date, zone);
        var business = new CreditTerm.Settings(settings.CreditTermDays, settings.TimeZone);
        var lines = Receivables.Lines(_context.Orders.AsNoTracking());

        // Who owes something overdue gets the overdue reminder, if any, and not this notice.
        var overdue = await CreditTerm.OverdueAsync(_context, lines, business, now, cancellationToken);
        var behind = (await overdue.Select(l => l.Phone).Distinct().ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        // What comes due by the last day of the notice, owed since before today: never the day of the sale.
        var dueBy = await CreditTerm.DueByAsync(_context, lines, business, now, settings.CreditDueSoonDaysBefore, cancellationToken);
        var candidates = (await dueBy
                .Where(l => l.OwedSince < startOfToday)
                .GroupBy(l => l.Phone)
                .Select(g => new { Phone = g.Key, OldestOwedSince = g.Min(l => l.OwedSince) })
                .ToListAsync(cancellationToken))
            .Where(a => !behind.Contains(a.Phone))
            .ToList();
        if (candidates.Count == 0)
            return 0;

        var candidatePhones = candidates.Select(a => a.Phone).ToList();
        var ownTerms = await CreditTerm.OwnTerms(_context.Customers.AsNoTracking())
            .Where(t => candidatePhones.Contains(t.Phone))
            .ToDictionaryAsync(t => t.Phone, t => t.Days, cancellationToken);
        CreditTerm.Settings TermOf(string phone) => business.For(ownTerms.TryGetValue(phone, out var days) ? days : null);

        // Who was told of this last day to pay already, or today of another one, or wrote BAJA is left out before the
        // limit: otherwise the same accounts, which get nothing, would take every place of every pass.
        var (noticed, noticedToday) = await NoticedAsync(today, now, cancellationToken);
        var optedOut = await CreditReminderPlanner.OptedOutAsync(_context, cancellationToken);
        var accounts = candidates
            .Select(a => new { a.Phone, DueOn = TermOf(a.Phone).DueOn(a.OldestOwedSince) })
            .Where(a => a.DueOn is { } dueOn && dueOn >= today
                && !noticed.Contains((a.Phone, dueOn)) && !noticedToday.Contains(a.Phone) && !optedOut.Contains(a.Phone))
            .Select(a => (a.Phone, DueOn: a.DueOn!.Value))
            .OrderBy(a => a.DueOn)
            .ThenBy(a => a.Phone, StringComparer.Ordinal)
            .Take(PassLimit)
            .ToList();
        if (accounts.Count == 0)
            return 0;

        var phones = accounts.Select(a => a.Phone).ToList();
        var owedLines = (await lines
                .Where(l => phones.Contains(l.Phone))
                .Select(l => new { l.Phone, l.OwedSince, l.Balance })
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.Phone, StringComparer.Ordinal);
        var names = await CreditReminderPlanner.NamesAsync(_context, _phones, phones, cancellationToken);

        // A deactivated template falls back to the default text, as the settings screen shows it.
        var template = settings.CreditDueSoonTemplateId is { } templateId
            ? await _context.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == templateId && t.Purpose == MessagePurpose.CreditDueSoon && t.IsActive, cancellationToken)
            : null;
        var body = template?.Body ?? MessagingDefaults.CreditDueSoonBody;
        var businessName = await CampaignLauncher.BusinessNameAsync(_context, settings.TenantId, cancellationToken);

        var queued = 0;
        foreach (var (phoneKey, dueOn) in accounts)
        {
            if (!_phones.TryNormalizeToE164(phoneKey, "MX", out var phone))
                continue;

            // What comes due that day, by the customer's term: every order of theirs counts by the same one.
            var term = TermOf(phoneKey);
            var mine = owedLines[phoneKey].ToList();
            var dueSoon = mine.Where(l => term.DueOn(l.OwedSince) == dueOn).Sum(l => l.Balance);
            var name = names.GetValueOrDefault(phoneKey);
            var values = Values(name, businessName, mine.Sum(l => l.Balance), dueSoon, dueOn);
            _context.OutgoingMessages.Add(new OutgoingMessage(
                settings.TenantId,
                MessagePurpose.CreditDueSoon,
                phone,
                name,
                MessageTemplateText.Render(body, values),
                template?.ProviderTemplateName,
                template?.LanguageCode ?? MessageTemplate.DefaultLanguageCode,
                MessageTemplateText.Parameters(body, values),
                notBefore: now,
                deduplicationKey: Key(phoneKey, dueOn, today)));
            queued++;
        }

        return queued;
    }

    public static IReadOnlyDictionary<string, string> Values(string? name, string businessName, decimal owed, decimal dueSoon, DateOnly dueOn)
        => new Dictionary<string, string>
        {
            [MessageTemplateText.Name] = MessageValues.FirstName(name),
            [MessageTemplateText.Business] = businessName,
            [MessageTemplateText.Amount] = OrderStatusLabels.FormatMoney(owed),
            [MessageTemplateText.DueSoon] = OrderStatusLabels.FormatMoney(dueSoon),
            [MessageTemplateText.Date] = MessageValues.FormatDate(new DateTimeOffset(dueOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
        };

    /// <summary>
    /// The accounts told already of each coming last day to pay, and those told today of any: today's notices count whatever
    /// became of them (one a day), and an earlier one counts sent, failed or skipped, so a customer who could not be reached
    /// is not told again every pass. A cancelled one does not count, nor one skipped for the business's own reasons (the
    /// notice was off, or the worker was stopped past the hour): the customer never got it.
    /// </summary>
    private async Task<(HashSet<(string Phone, DateOnly DueOn)> Noticed, HashSet<string> NoticedToday)> NoticedAsync(
        DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // A notice of a last day to pay from today on was queued at most the longest notice before it: the key's days
        // decide; the date only bounds the read, with a day to spare for the time zone.
        var readFrom = now.AddDays(-(TenantSettings.MaxCreditDueSoonDaysBefore + 2));
        var notices = await _context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.CreditDueSoon && m.DeduplicationKey != null && m.NextAttemptAt >= readFrom)
            .Select(m => new { Key = m.DeduplicationKey!, m.Status, m.LastError })
            .ToListAsync(cancellationToken);
        var noticed = new HashSet<(string, DateOnly)>();
        var noticedToday = new HashSet<string>(StringComparer.Ordinal);
        foreach (var notice in notices)
        {
            if (!TryParseKey(notice.Key, out var phone, out var dueOn, out var day))
                continue;
            if (day == today)
            {
                noticed.Add((phone, dueOn));
                noticedToday.Add(phone);
            }
            else if (dueOn >= today && Counts(notice.Status, notice.LastError))
            {
                noticed.Add((phone, dueOn));
            }
        }

        return (noticed, noticedToday);
    }

    private static bool Counts(OutgoingMessageStatus status, string? reason)
        => status != OutgoingMessageStatus.Cancelled
            && !(status == OutgoingMessageStatus.Skipped
                && reason is OutgoingMessageSender.CreditDueSoonOffReason or OutgoingMessageSender.CreditDueSoonLateReason);
}
