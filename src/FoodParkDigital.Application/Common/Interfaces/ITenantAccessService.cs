namespace FoodParkDigital.Application.Common.Interfaces;

public interface ITenantAccessService
{
    Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken);
}
