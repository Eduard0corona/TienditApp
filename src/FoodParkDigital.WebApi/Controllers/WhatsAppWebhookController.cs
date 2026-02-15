using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.WhatsApp.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Controllers;

[ApiController]
[Route("api/v1/whatsapp/webhook")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IApplicationDbContext _context;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IMediator mediator,
        IApplicationDbContext context,
        ILogger<WhatsAppWebhookController> logger)
    {
        _mediator = mediator;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Meta webhook verification endpoint.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge))
            return BadRequest("Invalid verification request.");

        // Look up tenant config by verify token
        var config = await _context.WhatsAppConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.VerifyToken == verifyToken);

        if (config is null)
        {
            _logger.LogWarning("Webhook verification failed: unknown verify token");
            return Unauthorized("Invalid verify token.");
        }

        _logger.LogInformation("Webhook verified for tenant {TenantId}", config.TenantId);
        return Ok(challenge);
    }

    /// <summary>
    /// Receives incoming WhatsApp messages from Meta Cloud API.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] JsonElement payload)
    {
        try
        {
            if (!payload.TryGetProperty("entry", out var entries))
                return Ok(); // Acknowledge but ignore

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes))
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value))
                        continue;

                    // Get phone_number_id to identify the tenant
                    var phoneNumberId = value
                        .GetProperty("metadata")
                        .GetProperty("phone_number_id")
                        .GetString();

                    if (string.IsNullOrEmpty(phoneNumberId))
                        continue;

                    // Find tenant by phone number ID
                    var config = await _context.WhatsAppConfigs
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.PhoneNumberId == phoneNumberId);

                    if (config is null)
                    {
                        _logger.LogWarning("Webhook received for unknown PhoneNumberId: {PhoneNumberId}", phoneNumberId);
                        continue;
                    }

                    // Extract contact name
                    string? profileName = null;
                    if (value.TryGetProperty("contacts", out var contacts))
                    {
                        var contact = contacts.EnumerateArray().FirstOrDefault();
                        if (contact.ValueKind != JsonValueKind.Undefined &&
                            contact.TryGetProperty("profile", out var profile) &&
                            profile.TryGetProperty("name", out var name))
                        {
                            profileName = name.GetString();
                        }
                    }

                    // Process each message
                    if (!value.TryGetProperty("messages", out var messages))
                        continue;

                    foreach (var msg in messages.EnumerateArray())
                    {
                        var msgType = msg.GetProperty("type").GetString();
                        if (msgType != "text")
                            continue; // Only process text messages for now

                        var from = msg.GetProperty("from").GetString();
                        var msgId = msg.GetProperty("id").GetString();
                        var body = msg.GetProperty("text").GetProperty("body").GetString();

                        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(msgId) || string.IsNullOrEmpty(body))
                            continue;

                        await _mediator.Send(new ProcessInboundMessageCommand
                        {
                            TenantId = config.TenantId,
                            FromPhone = from.StartsWith('+') ? from : $"+{from}",
                            ProfileName = profileName,
                            Body = body,
                            ExternalMessageId = msgId
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp webhook payload");
        }

        // Always return 200 to acknowledge receipt (Meta requires this)
        return Ok();
    }
}
