using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Downloads a file a customer sent by WhatsApp with the WhatsApp of the business in scope (Fase 31L, ADR 0092): without it
/// connected (off, or without its number or token), the file is unavailable.
/// </summary>
public sealed class WhatsAppMediaDownloader : IWhatsAppMediaDownloader
{
    private readonly ApplicationDbContext _context;
    private readonly WhatsAppCloudClient _client;

    public WhatsAppMediaDownloader(ApplicationDbContext context, WhatsAppCloudClient client)
    {
        _context = context;
        _client = client;
    }

    public async Task<WhatsAppMediaDownload> DownloadAsync(string mediaId, long maxBytes, CancellationToken cancellationToken)
    {
        // The business in scope (the outbox runs each event in its own): its filter leaves only its configuration.
        var config = await _context.WhatsAppConfigs
            .AsNoTracking()
            .Select(c => new { c.IsEnabled, c.PhoneNumberId, c.AccessToken })
            .FirstOrDefaultAsync(cancellationToken);
        if (config is not { IsEnabled: true } || string.IsNullOrEmpty(config.PhoneNumberId) || string.IsNullOrEmpty(config.AccessToken))
            return WhatsAppMediaDownload.Unavailable;

        return await _client.DownloadMediaAsync(
            new WhatsAppSenderAccount(config.PhoneNumberId, config.AccessToken), mediaId, maxBytes, cancellationToken);
    }
}
