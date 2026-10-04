using FoodParkDigital.Application.Common.Models;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IWhatsAppService
{
    Task<MessageSendResult> SendTemplateMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default);

    Task<MessageSendResult> SendTextMessageAsync(
        Guid tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}
