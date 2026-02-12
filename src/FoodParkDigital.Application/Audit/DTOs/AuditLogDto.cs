namespace FoodParkDigital.Application.Audit.DTOs;

public class AuditLogDto
{
    public Guid Id { get; set; }
    public string EntityName { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string PerformedBy { get; set; } = null!;
    public DateTimeOffset Timestamp { get; set; }
    public string? Metadata { get; set; }
}
