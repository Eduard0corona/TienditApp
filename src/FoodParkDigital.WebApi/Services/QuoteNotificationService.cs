using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Services;

/// <summary>The answers to the quotes of a business, to the live screens of whoever works them (<see cref="QuoteHub"/>, Fase 29R).</summary>
public class QuoteNotificationService : IQuoteNotificationService
{
    private readonly IHubContext<QuoteHub> _hubContext;

    public QuoteNotificationService(IHubContext<QuoteHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyQuoteAnswered(Guid tenantId, QuoteAnsweredNotice notice)
    {
        // Which quote and what the customer said; who they are and what they wrote stay in the quote.
        var payload = new { notice.QuoteId, notice.Number, notice.Revision, notice.Accepted };
        return _hubContext.Clients.Group(QuoteHub.GroupOf(tenantId)).SendAsync("QuoteAnswered", payload);
    }
}
