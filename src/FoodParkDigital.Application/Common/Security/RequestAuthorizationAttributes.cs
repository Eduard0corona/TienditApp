using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Common.Security;

/// <summary>
/// Who may send a MediatR request. Every request declares exactly one (a test enforces it) and
/// <see cref="Behaviors.AuthorizationBehavior{TRequest,TResponse}"/> checks it before validation and the
/// handler: a request without a rule is denied.
/// </summary>
public abstract class RequestAuthorizationAttribute : Attribute;

/// <summary>Public flows (sign in, register, confirmations) and requests sent by the system without a user.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowAnonymousRequestAttribute : RequestAuthorizationAttribute
{
    public AllowAnonymousRequestAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Why it needs no user, so a reviewer can check it.</summary>
    public string Reason { get; }
}

/// <summary>Any signed-in user, with or without a business (profile, own settings, switching business).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresAuthenticationAttribute : RequestAuthorizationAttribute;

/// <summary>Any active member of the current business.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresTenantMemberAttribute : RequestAuthorizationAttribute;

/// <summary>
/// A member of the current business with any of these permissions. Owners and admins of the business, and the
/// platform admin acting on it through the audited override, have every permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPermissionAttribute : RequestAuthorizationAttribute
{
    public RequiresPermissionAttribute(params TenantPermission[] anyOf)
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("At least one permission is required.", nameof(anyOf));
        AnyOf = anyOf;
    }

    public IReadOnlyList<TenantPermission> AnyOf { get; }
}

/// <summary>Owners and admins of the current business (configuration), or the platform admin.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresTenantManagerAttribute : RequestAuthorizationAttribute;

/// <summary>The platform admin only (all businesses).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPlatformAdminAttribute : RequestAuthorizationAttribute;

/// <summary>
/// The business must have the module (any of the flags). Combined with the rule above and checked for everyone,
/// the platform admin included: a module that is off is off.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    public RequiresCapabilityAttribute(TenantCapability anyOf)
    {
        AnyOf = anyOf;
    }

    public TenantCapability AnyOf { get; }
}
