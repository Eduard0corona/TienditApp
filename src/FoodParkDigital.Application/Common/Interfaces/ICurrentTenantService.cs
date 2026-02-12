namespace FoodParkDigital.Application.Common.Interfaces;

public interface ICurrentTenantService
{
    Guid TenantId { get; }
    void SetTenant(Guid tenantId);
}
