using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees;

/// <summary>Who keeps the business when the team changes: the owner stays, and someone always administers it.</summary>
public static class EmployeeAccessRules
{
    public const string OwnerIsPermanentMessage = "El dueño del negocio no se puede desactivar ni cambiar de rol.";
    public const string LastManagerMessage = "El negocio debe conservar al menos un administrador activo.";
    public const string SelfDemotionMessage = "No puedes quitarte a ti mismo el rol de administrador: pídeselo a otro administrador.";

    public static bool IsManagerRole(TenantUserRole role) => role is TenantUserRole.Owner or TenantUserRole.Admin;

    /// <summary>Another active owner or admin of the current business besides <paramref name="tenantUserId"/>.</summary>
    public static Task<bool> HasAnotherActiveManagerAsync(
        IApplicationDbContext context,
        Guid tenantId,
        Guid tenantUserId,
        CancellationToken cancellationToken) =>
        context.TenantUsers.AnyAsync(
            x => x.TenantId == tenantId
                 && x.Id != tenantUserId
                 && x.Status == TenantUserStatus.Active
                 && (x.Role == TenantUserRole.Owner || x.Role == TenantUserRole.Admin),
            cancellationToken);
}
