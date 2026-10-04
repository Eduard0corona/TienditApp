using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Security;
using MediatR;

namespace FoodParkDigital.Application.Common.Behaviors;

/// <summary>
/// Checks the request's declared rule (see <see cref="RequestAuthorizationAttribute"/>) before validation and
/// the handler. Deny by default: a request without exactly one rule never runs.
/// </summary>
public sealed class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public const string NotAllowedMessage = "No tienes permiso para hacer esto.";

    private readonly CurrentAccessContext _access;

    public AuthorizationBehavior(CurrentAccessContext access)
    {
        _access = access;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var rule = RequestAuthorizationRules.For(typeof(TRequest));
        switch (rule.Primary)
        {
            case null:
                throw new ForbiddenAccessException(NotAllowedMessage);
            case AllowAnonymousRequestAttribute:
                break;
            case RequiresAuthenticationAttribute:
                RequireAuthenticated();
                break;
            case RequiresPlatformAdminAttribute:
                RequireAuthenticated();
                if (!_access.IsPlatformAdmin)
                    throw new ForbiddenAccessException(NotAllowedMessage);
                break;
            case RequiresTenantManagerAttribute:
                await RequireMemberAsync(cancellationToken);
                if (!_access.IsTenantManager)
                    throw new ForbiddenAccessException("Solo el administrador del negocio puede hacer esto.");
                break;
            case RequiresTenantMemberAttribute:
                await RequireMemberAsync(cancellationToken);
                break;
            case RequiresPermissionAttribute permission:
                await RequireMemberAsync(cancellationToken);
                if (!_access.IsTenantManager)
                {
                    var granted = await _access.GetPermissionsAsync(cancellationToken);
                    if (!permission.AnyOf.Any(required => granted.Contains(required.ToString(), StringComparer.OrdinalIgnoreCase)))
                        throw new ForbiddenAccessException(NotAllowedMessage);
                }

                break;
            default:
                throw new ForbiddenAccessException(NotAllowedMessage);
        }

        if (rule.Capability is { } capability
            && !await _access.HasAnyCapabilityAsync(capability.AnyOf, cancellationToken))
        {
            throw new ForbiddenAccessException("El módulo no está activo en este negocio.");
        }

        return await next();
    }

    private void RequireAuthenticated()
    {
        if (!_access.IsAuthenticated)
            throw new UnauthorizedAccessException("Inicia sesión para continuar.");
    }

    private async Task RequireMemberAsync(CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        if (_access.TenantId == Guid.Empty)
            throw new ForbiddenAccessException("Elige un negocio para continuar.");
        if (_access.IsPlatformAdmin)
            return;

        var snapshot = await _access.GetSnapshotAsync(cancellationToken);
        if (snapshot is not { HasActiveAccess: true })
            throw new ForbiddenAccessException("No perteneces a este negocio o tu acceso está desactivado.");
    }
}
