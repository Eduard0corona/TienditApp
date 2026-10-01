using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class SmsService : ISmsService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v21.0";

    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmsService> _logger;
    private readonly bool _logSensitiveData;

    public SmsService(
        IApplicationDbContext context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IHostEnvironment environment,
        ILogger<SmsService> logger)
    {
        _context = context;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        // SMS bodies carry OTP codes: they, full phone numbers and provider responses are only logged in Development.
        _logSensitiveData = environment.IsDevelopment();
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        if (await TrySendVerificationByOwnerWhatsAppAsync(phoneNumber, code))
            return;

        var message = $"Tu codigo de verificacion FoodPark Digital es: {code}";
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

        var client = _httpClientFactory.CreateClient();
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
        var config = await _context.OwnerWhatsAppConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.IsEnabled && x.UseForOtp)
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

        var body = new
        {
            messaging_product = "whatsapp",
            to = toPhone,
            type = "template",
            template = new
            {
                name = config.OtpTemplateName,
                language = new { code = config.OtpTemplateLanguageCode },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = new[]
                        {
                            new
                            {
                                type = "text",
                                text = code
                            }
                        }
                    }
                }
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient("WhatsApp");
            var url = $"{GraphApiBaseUrl}/{config.PhoneNumberId}/messages";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);
            request.Content = JsonContent.Create(body, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            using var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OTP sent successfully via WhatsApp to {PhoneNumber}", LoggablePhone(toPhone));
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "WhatsApp OTP send failed for {PhoneNumber}. Status: {StatusCode}. Provider error code: {ProviderErrorCode}",
                LoggablePhone(toPhone),
                response.StatusCode,
                LogRedaction.GetProviderErrorCode(responseBody));
            LogProviderResponseInDevelopment("WhatsApp", responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while sending OTP via owner WhatsApp config.");
            return false;
        }
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
