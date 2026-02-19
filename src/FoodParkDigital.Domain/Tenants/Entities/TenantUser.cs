using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class TenantUser : AuditableEntity, ITenantScoped
{
    private TenantUser() { }

    public TenantUser(
        Guid tenantId,
        Guid userId,
        TenantUserRole role,
        TenantUserStatus status = TenantUserStatus.Active,
        IEnumerable<string>? permissionCodes = null)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = role;
        Status = status;
        PermissionCodes = TenantPermissionCatalog.ToStoredValue(permissionCodes);
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public TenantUserRole Role { get; private set; }
    public TenantUserStatus Status { get; private set; }
    public string? PermissionCodes { get; private set; }

    public void SetStatus(TenantUserStatus status) => Status = status;

    public void SetRole(TenantUserRole role) => Role = role;

    public void SetPermissionCodes(IEnumerable<string>? permissionCodes) =>
        PermissionCodes = TenantPermissionCatalog.ToStoredValue(permissionCodes);
}
