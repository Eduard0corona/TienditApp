using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Infrastructure.Services;

public class CurrentTenantService : ICurrentTenantService
{
    public Guid TenantId { get; private set; }
    public string? ConnectionString { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        TenantId = tenantId;
    }

    public void SetConnectionString(string connectionString)
    {
        ConnectionString = connectionString;
    }
}
