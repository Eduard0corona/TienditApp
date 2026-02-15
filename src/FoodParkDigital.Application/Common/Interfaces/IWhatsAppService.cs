using FoodParkDigital.Application.Common.Models;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IWhatsAppService
{
    Task<Result> SendTemplateMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default);

    Task<Result> SendTextMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}
