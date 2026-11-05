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

    /// <summary>
    /// Whether this member gets an email with each request online of the business (Fase 29N, ADR 0068), as they chose; null
    /// until they choose, when only the owners get them (<see cref="GetsRequestEmails"/>). Their own to choose: nobody else
    /// sets it for them.
    /// </summary>
    public bool? RequestEmails { get; private set; }

    /// <summary>What they chose or, until they choose, whether they own the business.</summary>
    public bool GetsRequestEmails => RequestEmails ?? Role == TenantUserRole.Owner;

    public void ChooseRequestEmails(bool enabled) => RequestEmails = enabled;

    public void SetStatus(TenantUserStatus status) => Status = status;

    public void SetRole(TenantUserRole role) => Role = role;

    public void SetPermissionCodes(IEnumerable<string>? permissionCodes) =>
        PermissionCodes = TenantPermissionCatalog.ToStoredValue(permissionCodes);

    /// <summary>The permissions an admin chose; none chosen is "sin acceso", not the role's defaults.</summary>
    public void SetExplicitPermissions(IEnumerable<string> permissionCodes) =>
        PermissionCodes = TenantPermissionCatalog.ToExplicitStoredValue(permissionCodes);
}
