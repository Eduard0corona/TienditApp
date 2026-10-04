using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Queues the WhatsApp reminder of each appointment that starts within the business's reminder window. Runs in
/// the business's context; the caller saves. The key includes the start time, so a rescheduled appointment gets
/// a new reminder and the old one is skipped when its turn comes.
/// </summary>
public sealed class AppointmentReminderPlanner
{
    /// <summary>An appointment about to start gets no reminder.</summary>
    public static readonly TimeSpan MinimumLeadTime = TimeSpan.FromMinutes(15);
    public const int MaxPerPass = 500;

    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public AppointmentReminderPlanner(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    public static string Key(Appointment appointment) => $"reminder:{appointment.Id:N}:{appointment.StartAt.UtcTicks}";

    /// <returns>Reminders queued.</returns>
    public async Task<int> QueueDueAsync(TenantSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.AppointmentRemindersEnabled)
            return 0;

        var now = _clock.UtcNow;
        var from = now.Add(MinimumLeadTime);
        var until = now.AddHours(settings.AppointmentReminderHoursBefore);
        var appointments = await _context.Appointments
            .AsNoTracking()
            .Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                && a.StartAt > from
                && a.StartAt <= until)
            .OrderBy(a => a.StartAt)
            .Take(MaxPerPass)
            .ToListAsync(cancellationToken);
        if (appointments.Count == 0)
            return 0;

        var keys = appointments.Select(Key).ToList();
        var alreadyQueued = (await _context.OutgoingMessages
                .AsNoTracking()
                .Where(m => m.DeduplicationKey != null && keys.Contains(m.DeduplicationKey))
                .Select(m => m.DeduplicationKey!)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        // A deactivated template falls back to the default text, as the settings screen shows it.
        var template = settings.AppointmentReminderTemplateId is { } templateId
            ? await _context.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(
                t => t.Id == templateId && t.Purpose == MessagePurpose.AppointmentReminder && t.IsActive, cancellationToken)
            : null;
        var body = template?.Body ?? MessagingDefaults.ReminderBody;
        var businessName = await CampaignLauncher.BusinessNameAsync(_context, settings.TenantId, cancellationToken);

        var queued = 0;
        foreach (var appointment in appointments)
        {
            var key = Key(appointment);
            if (alreadyQueued.Contains(key) || !_phones.TryNormalizeToE164(appointment.CustomerPhone, "MX", out var phone))
                continue;

            var values = Values(appointment, businessName, settings.TimeZone);
            _context.OutgoingMessages.Add(new OutgoingMessage(
                settings.TenantId,
                MessagePurpose.AppointmentReminder,
                phone,
                appointment.CustomerName,
                MessageTemplateText.Render(body, values),
                template?.ProviderTemplateName,
                template?.LanguageCode ?? MessageTemplate.DefaultLanguageCode,
                MessageTemplateText.Parameters(body, values),
                notBefore: now,
                deduplicationKey: key,
                appointmentId: appointment.Id));
            queued++;
        }

        return queued;
    }

    public static IReadOnlyDictionary<string, string> Values(Appointment appointment, string businessName, string? timeZone)
    {
        var local = MessageValues.ToLocal(appointment.StartAt, timeZone);
        return new Dictionary<string, string>
        {
            [MessageTemplateText.Name] = MessageValues.FirstName(appointment.CustomerName),
            [MessageTemplateText.Business] = businessName,
            [MessageTemplateText.Service] = appointment.ServiceName,
            [MessageTemplateText.Date] = MessageValues.FormatDate(local),
            [MessageTemplateText.Time] = MessageValues.FormatTime(local)
        };
    }
}
