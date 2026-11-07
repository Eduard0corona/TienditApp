namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// The live screens of whoever works the quotes of a business (Fase 29R, ADR 0072): best-effort, right after the commit. The
/// bell also reads the quotes every minute, and the email is the durable notice (Fase 29O).
/// </summary>
public interface IQuoteNotificationService
{
    /// <summary>A customer accepted or rejected a quote from its link: the bell and the quotes read it, and the screen tells it.</summary>
    Task NotifyQuoteAnswered(Guid tenantId, QuoteAnsweredNotice notice);
}

/// <summary>
/// What the screens get of an answer to a quote: which one and what the customer said, never who they are or what they wrote.
/// </summary>
/// <param name="Number">Its folio («COT-000012»).</param>
/// <param name="Revision">Its version, counted from 1.</param>
public sealed record QuoteAnsweredNotice(Guid QuoteId, string Number, int Revision, bool Accepted);
