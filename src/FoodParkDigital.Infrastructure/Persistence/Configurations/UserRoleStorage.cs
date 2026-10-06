using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// How the role of an account is stored. The platform admin keeps the 0 it always had, so the rows already saved and
/// the version being replaced read the same; every other role is stored as its number. An account without a role is
/// never saved: it would have been the platform admin before.
/// </summary>
internal static class UserRoleStorage
{
    internal const int StoredSuperAdmin = 0;

    public static readonly ValueConverter<UserRole, int> Converter = new(
        role => ToStored(role),
        stored => FromStored(stored));

    internal static int ToStored(UserRole role) => role switch
    {
        UserRole.SuperAdmin => StoredSuperAdmin,
        UserRole.None => throw new InvalidOperationException("Una cuenta no puede guardarse sin rol."),
        _ => (int)role
    };

    internal static UserRole FromStored(int stored) =>
        stored == StoredSuperAdmin ? UserRole.SuperAdmin : (UserRole)stored;
}
