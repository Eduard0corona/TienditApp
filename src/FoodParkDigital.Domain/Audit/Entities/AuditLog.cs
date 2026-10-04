using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Audit.Entities;

public class AuditLog : BaseEntity, ITenantScoped
{
    private AuditLog() { }

    /// <param name="timestamp">When it happened; entries written later by a background handler pass the event time.</param>
    public AuditLog(
        Guid tenantId,
        string entityName,
        string entityId,
        string action,
        string performedBy,
        string? metadata = null,
        DateTimeOffset? timestamp = null)
    {
        TenantId = tenantId;
        EntityName = entityName;
        EntityId = entityId;
        Action = action;
        PerformedBy = performedBy;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        Metadata = metadata;
    }

    public Guid TenantId { get; private set; }
    public string EntityName { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public string PerformedBy { get; private set; } = null!;
    public DateTimeOffset Timestamp { get; private set; }
    public string? Metadata { get; private set; }
}
