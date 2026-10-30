using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>Sends with the WhatsApp of the business in scope, through the single Meta client.</summary>
public class WhatsAppService : IWhatsAppService, IWhatsAppDocumentSender
{
    public const string NotConfiguredError = "WhatsApp no está configurado para este negocio.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly WhatsAppCloudClient _client;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        WhatsAppCloudClient client,
        ILogger<WhatsAppService> logger)
    {
        _context = context;
        _tenant = tenant;
        _client = client;
        _logger = logger;
    }

    public async Task<MessageSendResult> SendTemplateMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(tenantId, cancellationToken);
        if (account is null)
            return MessageSendResult.Failed(NotConfiguredError, isTransient: false);

        return Record(tenantId, await _client.SendTemplateAsync(
            account, phoneNumber, templateName, languageCode, parameters, cancellationToken));
    }

    public async Task<MessageSendResult> SendTextMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(tenantId, cancellationToken);
        if (account is null)
            return MessageSendResult.Failed(NotConfiguredError, isTransient: false);

        return Record(tenantId, await _client.SendTextAsync(account, phoneNumber, message, cancellationToken));
    }

    /// <summary>
    /// Uploads the document and sends it (Fase 28M): with the template, in its document header; without one, with its
    /// caption. A failed upload is the message's outcome, transient or not.
    /// </summary>
    public async Task<MessageSendResult> SendDocumentAsync(
        Guid tenantId,
        string phoneNumber,
        WhatsAppDocument document,
        string caption,
        string? templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(tenantId, cancellationToken);
        if (account is null)
            return MessageSendResult.Failed(NotConfiguredError, isTransient: false);

        var upload = await _client.UploadMediaAsync(account, document, cancellationToken);
        if (upload.IsFailure)
            return Record(tenantId, upload);

        var uploaded = new WhatsAppMediaDocument(upload.ProviderMessageId!, document.FileName);
        return Record(tenantId, templateName is null
            ? await _client.SendDocumentAsync(account, phoneNumber, uploaded, caption, cancellationToken)
            : await _client.SendTemplateAsync(account, phoneNumber, templateName, languageCode, parameters, cancellationToken, uploaded));
    }

    /// <summary>
    /// Every caller runs in the scope of the business it sends for (a request, the outbox, the messaging worker, the
    /// webhook). <c>WhatsAppConfig</c> has no tenant filter (the webhook and login read it before there is a
    /// business), so the scope is checked here: asking for another business finds no token instead of using it.
    /// </summary>
    private async Task<WhatsAppSenderAccount?> GetAccountAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId != _tenant.TenantId)
        {
            _logger.LogWarning(
                "WhatsApp send for tenant {TenantId} refused: the business in scope is {ScopeTenantId}",
                tenantId,
                _tenant.TenantId);
            return null;
        }

        var config = await _context.WhatsAppConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken);

        if (config is null || !config.IsEnabled)
        {
            _logger.LogWarning("WhatsApp not configured or disabled for tenant {TenantId}", tenantId);
            return null;
        }

        if (string.IsNullOrEmpty(config.AccessToken) || string.IsNullOrEmpty(config.PhoneNumberId))
        {
            _logger.LogWarning("WhatsApp credentials incomplete for tenant {TenantId}", tenantId);
            return null;
        }

        return new WhatsAppSenderAccount(config.PhoneNumberId, config.AccessToken);
    }

    private static MessageSendResult Record(Guid tenantId, MessageSendResult result)
    {
        var outcome = result.IsSuccess
            ? MetricOutcomes.Sent
            : result.IsTransient ? MetricOutcomes.TransientFailure : MetricOutcomes.Rejected;
        TienditAppMetrics.RecordMessageSent("whatsapp", outcome, tenantId);
        return result;
    }
}
