using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments;

/// <summary>The service and the team member an appointment books, checked against the business's catalog and team.</summary>
public static class AppointmentResources
{
    public sealed record Resolved(string ServiceName, string? StaffName);

    public static async Task<Result<Resolved>> ResolveAsync(
        IApplicationDbContext context,
        Guid? serviceProductId,
        string? serviceName,
        Guid? staffUserId,
        string? staffName,
        CancellationToken cancellationToken)
    {
        var resolvedService = serviceName?.Trim();
        if (serviceProductId is { } productId)
        {
            // Products are filtered by business: a service of another business is not found.
            var productName = await context.Products
                .AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);
            if (productName is null)
                return Result.Failure<Resolved>("El servicio no existe en el catálogo del negocio.");
            resolvedService = productName;
        }

        if (string.IsNullOrWhiteSpace(resolvedService))
            return Result.Failure<Resolved>("Elige el servicio de la cita.");

        var resolvedStaff = string.IsNullOrWhiteSpace(staffName) ? null : staffName.Trim();
        if (staffUserId is { } userId)
        {
            // Memberships are filtered by business: only an active member of this one can attend.
            var member = await context.TenantUsers
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == TenantUserStatus.Active)
                .Join(context.Users, m => m.UserId, u => u.Id, (m, u) => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync(cancellationToken);
            if (member is null)
                return Result.Failure<Resolved>("La persona elegida no está activa en el equipo del negocio.");
            resolvedStaff = $"{member.FirstName} {member.LastName}".Trim();
        }

        return Result.Success(new Resolved(resolvedService!, resolvedStaff));
    }

    /// <summary>Another open appointment of the same team member that overlaps the time.</summary>
    public static Task<bool> OverlapsAsync(
        IApplicationDbContext context,
        Guid? staffUserId,
        string? staffName,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        if (staffUserId is null && string.IsNullOrWhiteSpace(staffName))
            return Task.FromResult(false);

        var query = context.Appointments
            .AsNoTracking()
            .Where(x => x.Status != AppointmentStatus.Cancelled && x.Status != AppointmentStatus.NoShow)
            .Where(x => startAt < x.EndAt && endAt > x.StartAt);
        if (excludeAppointmentId is { } excluded)
            query = query.Where(x => x.Id != excluded);
        query = staffUserId is { } userId
            ? query.Where(x => x.StaffUserId == userId)
            : query.Where(x => x.StaffUserId == null && x.StaffName == staffName);
        return query.AnyAsync(cancellationToken);
    }
}
