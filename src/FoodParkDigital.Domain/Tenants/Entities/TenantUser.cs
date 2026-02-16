using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class TenantUser : AuditableEntity, ITenantScoped
{
    private TenantUser() { }

    public TenantUser(Guid tenantId, Guid userId, TenantUserRole role, TenantUserStatus status = TenantUserStatus.Active)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        Status = status;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public TenantUserRole Role { get; private set; }
    public TenantUserStatus Status { get; private set; }

    public void SetStatus(TenantUserStatus status) => Status = status;

    public void SetRole(TenantUserRole role) => Role = role;
}
