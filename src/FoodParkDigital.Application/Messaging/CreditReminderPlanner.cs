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
/// Queues the WhatsApp reminder of what each customer owes past their term (Fase 26C, ADR 0035): one per customer (by
/// phone), the first once something is overdue and then every few days while it stays overdue. Only during the business's
/// day, never to who wrote BAJA, and only while WhatsApp is connected. Runs in the business's context; the caller saves.
/// The key includes the business day, so two passes (or two instances) never queue the same reminder twice.
/// </summary>
public sealed class CreditReminderPlanner
{
    /// <summary>Reminders are queued from this hour of the business's day…</summary>
    public static readonly TimeOnly QueueFrom = new(10, 0);
    /// <summary>…until this one, so none is queued at night.</summary>
    public static readonly TimeOnly QueueUntil = new(19, 0);
    /// <summary>A reminder still waiting at this hour of its day (the worker was stopped) is no longer sent.</summary>
    public static readonly TimeOnly SendUntil = new(20, 0);
    public const int MaxPerPass = 500;
    private const string KeyPrefix = "credit:";

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public CreditReminderPlanner(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    /// <summary>Accounts queued per pass and business, at most: the rest go in the next passes.</summary>
    internal int PassLimit { get; init; } = MaxPerPass;

    public static string Key(string phoneKey, DateOnly businessDay) => $"{KeyPrefix}{phoneKey}:{businessDay:yyyyMMdd}";

    /// <returns>Reminders queued.</returns>
    public async Task<int> QueueDueAsync(TenantSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.CreditRemindersEnabled)
            return 0;

        var now = _clock.UtcNow;
        var zone = TenantDateTimeResolver.Resolve(settings.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var timeOfDay = TimeOnly.FromDateTime(local.DateTime);
        if (timeOfDay < QueueFrom || timeOfDay >= QueueUntil)
            return 0;
        // A reminder without WhatsApp would only fail: none is queued until it is connected again.
        if (!await CampaignRules.IsWhatsAppConnectedAsync(_context, settings.TenantId, cancellationToken))
            return 0;

        var today = DateOnly.FromDateTime(local.Date);
        var business = new CreditTerm.Settings(settings.CreditTermDays, settings.TimeZone);
        var lines = Receivables.Lines(_context.Orders.AsNoTracking());
        var overdue = await CreditTerm.OverdueAsync(_context, lines, business, now, cancellationToken);
        var candidates = await overdue
            .GroupBy(l => l.Phone)
            .Select(g => new { Phone = g.Key, Overdue = g.Sum(l => l.Balance), OldestOwedSince = g.Min(l => l.OwedSince) })
            .OrderByDescending(a => a.Overdue)
            .ThenBy(a => a.Phone)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return 0;

        // Who was reminded within the days or wrote BAJA is left out before the limit: otherwise the same accounts, which
        // get nothing, would take every place of every pass and the rest would never be reminded.
        var reminded = await RemindedAsync(today, settings.CreditReminderEveryDays, now, cancellationToken);
        var optedOut = (await _context.MessagingOptOuts.AsNoTracking()
                .Where(o => o.ResubscribedAt == null)
                .Select(o => o.Phone)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var accounts = candidates
            .Where(a => !reminded.Contains(a.Phone) && !optedOut.Contains(a.Phone))
            .Take(PassLimit)
            .ToList();
        if (accounts.Count == 0)
            return 0;

        var phones = accounts.Select(a => a.Phone).ToList();
        var owed = await lines
            .Where(l => phones.Contains(l.Phone))
            .GroupBy(l => l.Phone)
            .Select(g => new { Phone = g.Key, Owed = g.Sum(l => l.Balance) })
            .ToDictionaryAsync(a => a.Phone, a => a.Owed, cancellationToken);
        var ownTerms = await CreditTerm.OwnTerms(_context.Customers.AsNoTracking())
            .Where(t => phones.Contains(t.Phone))
            .ToDictionaryAsync(t => t.Phone, t => t.Days, cancellationToken);
        var names = await NamesAsync(phones, cancellationToken);

        // A deactivated template falls back to the default text, as the settings screen shows it.
        var template = settings.CreditReminderTemplateId is { } templateId
            ? await _context.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == templateId && t.Purpose == MessagePurpose.CreditReminder && t.IsActive, cancellationToken)
            : null;
        var body = template?.Body ?? MessagingDefaults.CreditReminderBody;
        var businessName = await CampaignLauncher.BusinessNameAsync(_context, settings.TenantId, cancellationToken);

        var queued = 0;
        foreach (var account in accounts)
        {
            // The oldest last day to pay that passed, by the customer's term: every order of theirs counts by the same one.
            // None when their own term was removed since what is overdue was read and the business has none.
            var term = business.For(ownTerms.TryGetValue(account.Phone, out var days) ? days : null);
            if (term.DueOn(account.OldestOwedSince) is not { } dueOn
                || !_phones.TryNormalizeToE164(account.Phone, "MX", out var phone))
                continue;

            var name = names.GetValueOrDefault(account.Phone);
            var values = Values(name, businessName, owed.GetValueOrDefault(account.Phone, account.Overdue), account.Overdue, dueOn);
            _context.OutgoingMessages.Add(new OutgoingMessage(
                settings.TenantId,
                MessagePurpose.CreditReminder,
                phone,
                name,
                MessageTemplateText.Render(body, values),
                template?.ProviderTemplateName,
                template?.LanguageCode ?? MessageTemplate.DefaultLanguageCode,
                MessageTemplateText.Parameters(body, values),
                notBefore: now,
                deduplicationKey: Key(account.Phone, today)));
            queued++;
        }

        return queued;
    }

    public static IReadOnlyDictionary<string, string> Values(string? name, string businessName, decimal owed, decimal overdue, DateOnly dueOn)
        => new Dictionary<string, string>
        {
            [MessageTemplateText.Name] = MessageValues.FirstName(name),
            [MessageTemplateText.Business] = businessName,
            [MessageTemplateText.Amount] = OrderStatusLabels.FormatMoney(owed),
            [MessageTemplateText.Overdue] = OrderStatusLabels.FormatMoney(overdue),
            [MessageTemplateText.Date] = MessageValues.FormatDate(new DateTimeOffset(dueOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
        };

    /// <summary>
    /// The phone keys that must wait: today's key is taken whatever became of its reminder (one per day), and within the
    /// last <paramref name="everyDays"/> business days a reminder counts sent, failed or skipped, so a customer who could
    /// not be reached is not asked again every pass. A cancelled one does not count, nor one skipped for the business's
    /// own reasons (the reminders were off, or the worker was stopped past the hour): the customer never got it.
    /// </summary>
    private async Task<HashSet<string>> RemindedAsync(DateOnly today, int everyDays, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = today.AddDays(1 - everyDays);
        // The key's day decides; the date only bounds the read, with a day to spare for the time zone. The next attempt
        // starts when the reminder is queued and only moves later.
        var readFrom = now.AddDays(-(everyDays + 1));
        var reminders = await _context.OutgoingMessages.AsNoTracking()
            .Where(m => m.Purpose == MessagePurpose.CreditReminder && m.DeduplicationKey != null && m.NextAttemptAt >= readFrom)
            .Select(m => new { Key = m.DeduplicationKey!, m.Status, m.LastError })
            .ToListAsync(cancellationToken);
        var reminded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reminder in reminders)
        {
            if (!TryParseKey(reminder.Key, out var phone, out var day) || day < since)
                continue;
            if (day == today || CountsForCadence(reminder.Status, reminder.LastError))
                reminded.Add(phone);
        }

        return reminded;
    }

    private static bool CountsForCadence(OutgoingMessageStatus status, string? reason)
        => status != OutgoingMessageStatus.Cancelled
            && !(status == OutgoingMessageStatus.Skipped
                && reason is OutgoingMessageSender.CreditRemindersOffReason or OutgoingMessageSender.CreditReminderLateReason);

    private static bool TryParseKey(string key, out string phone, out DateOnly day)
    {
        phone = string.Empty;
        day = default;
        var dayStart = key.LastIndexOf(':') + 1;
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal) || dayStart <= KeyPrefix.Length + 1)
            return false;
        phone = key[KeyPrefix.Length..(dayStart - 1)];
        return DateOnly.TryParseExact(key[dayStart..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
    }

    /// <summary>The customer record's name for each phone, the active one first; else the name on their latest order owed.</summary>
    private async Task<Dictionary<string, string?>> NamesAsync(IReadOnlyList<string> phones, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        var forms = phones.SelectMany(CustomerPhones.StoredForms).Distinct().ToList();
        var records = await _context.Customers.AsNoTracking()
            .Where(c => !c.IsPublicGeneral && c.Phone != null && forms.Contains(c.Phone))
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.CreditLimit != null)
            .Select(c => new { c.Phone, c.Name })
            .ToListAsync(cancellationToken);
        foreach (var record in records)
        {
            if (CustomerPhones.TryNormalize(_phones, record.Phone, out var key))
                names.TryAdd(key, record.Name);
        }

        var variants = phones.SelectMany(MessagingPhone.Variants).Distinct().ToList();
        var latest = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerPhone != null && o.CustomerName != null && variants.Contains(o.CustomerPhone))
            .Where(Receivables.IsOwed)
            .Select(o => new { o.CustomerPhone, o.CustomerName, o.CreatedAt })
            .ToListAsync(cancellationToken);
        foreach (var order in latest.OrderByDescending(o => o.CreatedAt))
            names.TryAdd(MessagingPhone.ToKey(order.CustomerPhone!), order.CustomerName);
        return names;
    }
}
