namespace FoodParkDigital.Domain.Common;

public interface ITenantScoped
{
    Guid TenantId { get; }
}
