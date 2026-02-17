using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Customers.Enums;

namespace FoodParkDigital.Domain.Customers.Entities;

public class TemporaryCustomer : AuditableEntity, ITenantScoped
{
    private TemporaryCustomer() { }

    public TemporaryCustomer(Guid tenantId, string phoneNormalized, string? name)
    {
        TenantId = tenantId;
        PhoneNormalized = phoneNormalized;
        Name = name;
        Status = TemporaryCustomerStatus.PendingInvited;
    }

    public Guid TenantId { get; private set; }
    public string PhoneNormalized { get; private set; } = null!;
    public string? Name { get; private set; }
    public TemporaryCustomerStatus Status { get; private set; }
    public Guid? ClaimedUserId { get; private set; }

    public void MarkInvited() => Status = TemporaryCustomerStatus.Invited;

    public void Claim(Guid userId)
    {
        ClaimedUserId = userId;
        Status = TemporaryCustomerStatus.Claimed;
    }

    public void UpdateName(string? name) => Name = name;
}
