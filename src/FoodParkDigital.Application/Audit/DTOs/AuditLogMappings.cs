using System.Linq.Expressions;
using FoodParkDigital.Domain.Audit.Entities;

namespace FoodParkDigital.Application.Audit.DTOs;

public static class AuditLogMappings
{
    public static readonly Expression<Func<AuditLog, AuditLogDto>> AsDto = log => new AuditLogDto
    {
        Id = log.Id,
        EntityName = log.EntityName,
        EntityId = log.EntityId,
        Action = log.Action,
        PerformedBy = log.PerformedBy,
        Timestamp = log.Timestamp,
        Metadata = log.Metadata
    };
}
