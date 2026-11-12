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
    /// Whether this member gets an email with each request online of the business (Fase 29N, ADR 0068) and, since the Fase
    /// 29O (ADR 0069), with each appointment its customer cancels, as they chose; null until they choose, when only the owners
    /// get them (<see cref="GetsRequestEmails"/>). Theirs to choose and, since the Fase 29Q (ADR 0071), whoever runs the team's,
    /// except the owner's.
    /// </summary>
    public bool? RequestEmails { get; private set; }

    /// <summary>What they chose or, until they choose, whether they own the business.</summary>
    public bool GetsRequestEmails => RequestEmails ?? Role == TenantUserRole.Owner;

    public void ChooseRequestEmails(bool enabled) => RequestEmails = enabled;

    /// <summary>
    /// Whether this member gets an email each time a customer accepts or rejects a quote of the business from its link (Fase
    /// 29O, ADR 0069), as they chose; null until they choose, when only the owners get them (<see cref="GetsQuoteReplyEmails"/>).
    /// Theirs to choose and, since the Fase 29Q (ADR 0071), whoever runs the team's, except the owner's.
    /// </summary>
    public bool? QuoteReplyEmails { get; private set; }

    /// <summary>What they chose or, until they choose, whether they own the business.</summary>
    public bool GetsQuoteReplyEmails => QuoteReplyEmails ?? Role == TenantUserRole.Owner;

    public void ChooseQuoteReplyEmails(bool enabled) => QuoteReplyEmails = enabled;

    /// <summary>
    /// Whether this member gets an email each time a customer cancels an online order of the business before it was accepted
    /// (Fase 30B, ADR 0082), or asks it to cancel one (Fase 31B), and the reminder of the one that waits 15 minutes to be
    /// accepted (Fase 31K, ADR 0091), as they chose; null until they choose, when only the owners get them
    /// (<see cref="GetsOrderCancellationEmails"/>). Theirs to choose and whoever runs the team's, except the owner's.
    /// </summary>
    public bool? OrderCancellationEmails { get; private set; }

    /// <summary>What they chose or, until they choose, whether they own the business.</summary>
    public bool GetsOrderCancellationEmails => OrderCancellationEmails ?? Role == TenantUserRole.Owner;

    public void ChooseOrderCancellationEmails(bool enabled) => OrderCancellationEmails = enabled;

    /// <summary>
    /// Whether this member gets an email each time a customer writes a new review of the business (Fase 31K, ADR 0091), as
    /// they chose; null until they choose, when only the owners get them (<see cref="GetsReviewEmails"/>). Only its owners
    /// and admins read and answer the reviews, so only they get them. Theirs to choose and whoever runs the team's, except
    /// the owner's.
    /// </summary>
    public bool? ReviewEmails { get; private set; }

    /// <summary>What they chose or, until they choose, whether they own the business.</summary>
    public bool GetsReviewEmails => ReviewEmails ?? Role == TenantUserRole.Owner;

    public void ChooseReviewEmails(bool enabled) => ReviewEmails = enabled;

    public void SetStatus(TenantUserStatus status) => Status = status;

    public void SetRole(TenantUserRole role) => Role = role;

    public void SetPermissionCodes(IEnumerable<string>? permissionCodes) =>
        PermissionCodes = TenantPermissionCatalog.ToStoredValue(permissionCodes);

    /// <summary>The permissions an admin chose; none chosen is "sin acceso", not the role's defaults.</summary>
    public void SetExplicitPermissions(IEnumerable<string> permissionCodes) =>
        PermissionCodes = TenantPermissionCatalog.ToExplicitStoredValue(permissionCodes);
}
