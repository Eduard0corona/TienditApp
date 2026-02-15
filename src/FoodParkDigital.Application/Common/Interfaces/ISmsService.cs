namespace FoodParkDigital.Application.Common.Interfaces;

public interface ISmsService
{
    Task SendVerificationCodeAsync(string phoneNumber, string code);
    Task SendSmsAsync(string phoneNumber, string message);
}
