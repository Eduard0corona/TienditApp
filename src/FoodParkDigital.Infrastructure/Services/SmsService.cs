using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace FoodParkDigital.Infrastructure.Services;

public class SmsService : ISmsService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmsService> _logger;

    public SmsService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<SmsService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        var message = $"Tu código de verificación FoodPark Digital es: {code}";
        return SendSmsAsync(phoneNumber, message);
    }

    public async Task SendSmsAsync(string phoneNumber, string message)
    {
        var provider = _configuration["SmsSettings:Provider"]?.Trim();
        if (!string.Equals(provider, "Twilio", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("SMS provider not configured for runtime sending. Message logged for {PhoneNumber}.", phoneNumber);
            _logger.LogInformation("SMS body: {Message}", message);
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
            _logger.LogInformation("SMS body: {Message}", message);
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
}
