using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Enums;
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
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SmsService> _logger;

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
        _environment = environment;
        _logger = logger;
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
            _logger.LogInformation("SMS provider not configured for runtime sending. Message not sent to {PhoneNumber}.", phoneNumber);
            LogBodyInDevelopment(message);
            return;
        }

        var accountSid = _configuration["SmsSettings:AccountSid"];
        var authToken = _configuration["SmsSettings:AuthToken"];
        var fromNumber = _configuration["SmsSettings:FromNumber"];

        if (string.IsNullOrWhiteSpace(accountSid)
            || string.IsNullOrWhiteSpace(authToken)
            || string.IsNullOrWhiteSpace(fromNumber))
        {
            _logger.LogWarning("Twilio SMS settings are incomplete. Message not sent to {PhoneNumber}.", phoneNumber);
            LogBodyInDevelopment(message);
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
            _logger.LogInformation("SMS sent successfully to {PhoneNumber}", phoneNumber);
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogError(
            "Twilio SMS send failed for {PhoneNumber}. Status: {StatusCode}. Body: {Body}",
            phoneNumber,
            response.StatusCode,
            body);
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
                _logger.LogInformation("OTP sent successfully via WhatsApp to {PhoneNumber}", toPhone);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "WhatsApp OTP send failed for {PhoneNumber}. Status: {StatusCode}. Body: {Body}",
                toPhone,
                response.StatusCode,
                responseBody);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while sending OTP via owner WhatsApp config.");
            return false;
        }
    }

    // Message bodies carry verification codes; they are only written to the log in Development.
    private void LogBodyInDevelopment(string message)
    {
        if (_environment.IsDevelopment())
            _logger.LogInformation("SMS body (Development only): {Message}", message);
    }
}
