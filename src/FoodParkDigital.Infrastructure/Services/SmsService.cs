using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class SmsService : ISmsService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmsService> _logger;

    public SmsService(IConfiguration configuration, ILogger<SmsService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        var message = $"Tu código de verificación FoodPark Digital es: {code}";
        return SendSmsAsync(phoneNumber, message);
    }

    public Task SendSmsAsync(string phoneNumber, string message)
    {
        // TODO: Integrar con Twilio, Vonage, MessageBird u otro proveedor SMS
        // var accountSid = _configuration["SmsSettings:AccountSid"];
        // var authToken = _configuration["SmsSettings:AuthToken"];
        // var fromNumber = _configuration["SmsSettings:FromNumber"];

        _logger.LogInformation("Enviando SMS a {PhoneNumber}: {Message}", phoneNumber, message);
        _logger.LogInformation("SMS enviado exitosamente a {PhoneNumber}", phoneNumber);

        return Task.CompletedTask;
    }
}
