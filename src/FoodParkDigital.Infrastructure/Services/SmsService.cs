using System.Net.Http.Headers;
using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Verification codes: by WhatsApp with the platform's account when there is one (through the single Meta client),
/// otherwise by SMS (Twilio).
/// </summary>
public class SmsService : ISmsService
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsAppCloudClient _whatsApp;
    private readonly ILogger<SmsService> _logger;
    private readonly bool _logSensitiveData;

    public SmsService(
        IApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        WhatsAppCloudClient whatsApp,
        IHostEnvironment environment,
        ILogger<SmsService> logger)
    {
        _context = context;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _whatsApp = whatsApp;
        _logger = logger;
        // SMS bodies carry OTP codes: they, full phone numbers and provider responses are only logged in Development.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        if (await TrySendVerificationByOwnerWhatsAppAsync(phoneNumber, code))
            return;

        var message = $"Tu código de verificación de TienditApp es: {code}";
        await SendSmsAsync(phoneNumber, message);
    }

    public async Task SendSmsAsync(string phoneNumber, string message)
    {
        var provider = _configuration["SmsSettings:Provider"]?.Trim();
        if (!string.Equals(provider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("SMS provider not configured for runtime sending. Message not sent to {PhoneNumber}.", LoggablePhone(phoneNumber));
            LogSmsBodyInDevelopment(message);
            return;
        }

        var accountSid = _configuration["SmsSettings:AccountSid"];
        var authToken = _configuration["SmsSettings:AuthToken"];
        var fromNumber = _configuration["SmsSettings:FromNumber"];

        if (string.IsNullOrWhiteSpace(accountSid)
            || string.IsNullOrWhiteSpace(authToken)
            || string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogWarning("Twilio SMS settings are incomplete. Message not sent to {PhoneNumber}.", LoggablePhone(phoneNumber));
            LogSmsBodyInDevelopment(message);
            return;
        }

        var client = _httpClientFactory.CreateClient("Twilio");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{accountSid}:{authToken}"));
        var endpoint = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = phoneNumber,
            ["From"] = fromNumber,
            ["Body"] = message
        });

        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("SMS sent successfully to {PhoneNumber}", LoggablePhone(phoneNumber));
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogError(
            "Twilio SMS send failed for {PhoneNumber}. Status: {StatusCode}. Provider error code: {ProviderErrorCode}",
            LoggablePhone(phoneNumber),
            response.StatusCode,
            LogRedaction.GetProviderErrorCode(body));
        LogProviderResponseInDevelopment("Twilio", body);
    }

    private async Task<bool> TrySendVerificationByOwnerWhatsAppAsync(string phoneNumber, string code)
    {
        // Platform OTPs may only go out through a WhatsApp account owned by an active SUPER_ADMIN;
        // a tenant owner's account must never receive other users' verification codes.
        var config = await _context.OwnerWhatsAppConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.UseForOtp)
            .Where(x => _context.Users
                .IgnoreQueryFilters()
                .Any(u => u.Id == x.UserId && u.Role == UserRole.SuperAdmin && u.IsActive))
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync();

        if (config is null)
            return false;

        if (string.IsNullOrWhiteSpace(config.AccessToken)
            || string.IsNullOrWhiteSpace(config.PhoneNumberId)
            || string.IsNullOrWhiteSpace(config.OtpTemplateName)
            || string.IsNullOrWhiteSpace(config.OtpTemplateLanguageCode))
        {
            _logger.LogWarning("Owner WhatsApp config exists but is incomplete for OTP delivery.");
            return false;
        }

        var toPhone = phoneNumber.StartsWith('+')
            ? phoneNumber
            : $"+{phoneNumber}";

        // The client logs the outcome with the recipient masked; a failure falls back to SMS.
        var result = await _whatsApp.SendTemplateAsync(
            new WhatsAppSenderAccount(config.PhoneNumberId, config.AccessToken),
            toPhone,
            config.OtpTemplateName,
            config.OtpTemplateLanguageCode,
            [code],
            CancellationToken.None);
        return result.IsSuccess;
    }

    private string LoggablePhone(string phoneNumber)
        => _logSensitiveData ? phoneNumber : LogRedaction.MaskPhoneNumber(phoneNumber);

    // Without an SMS provider, local development reads OTP codes from this line.
    private void LogSmsBodyInDevelopment(string message)
    {
        if (_logSensitiveData)
            _logger.LogInformation("SMS body: {Message}", message);
    }

    // Provider responses can echo phone numbers or message content.
    private void LogProviderResponseInDevelopment(string provider, string responseBody)
    {
        if (_logSensitiveData)
            _logger.LogInformation("{Provider} response body: {Body}", provider, responseBody);
    }
}
