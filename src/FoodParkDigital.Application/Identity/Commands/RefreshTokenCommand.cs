using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record RefreshTokenCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

/// <summary>
/// Renews the session of a refresh token: same session, next token. A token already replaced that comes back was
/// copied, so the session ends for whoever holds its newest token too.
/// </summary>
public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    public const string InvalidSessionError = "La sesión ya no es válida. Inicia sesión de nuevo.";
    public const string ConcurrentRenewalError = "La sesión se acaba de renovar en otra pestaña. Intenta de nuevo.";

    /// <summary>
    /// How long a replaced token counts as a renewal that lost a race (two requests of the same browser, one a moment
    /// later) rather than as a copy. It gets no tokens either way; it only does not end the session.
    /// </summary>
    public static readonly TimeSpan ConcurrentRenewalWindow = TimeSpan.FromSeconds(10);

    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITenantAccessService _tenantAccessService;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IJwtTokenService jwtTokenService,
        ITenantAccessService tenantAccessService,
        IDateTimeProvider clock,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _tenantAccessService = tenantAccessService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var presented = await UserSessions.FindAsync(_context, request.RefreshToken, cancellationToken);
        if (presented is null)
            return Result.Failure<AuthResponse>(InvalidSessionError);

        // Issued by the previous version: from now on only its hash is stored.
        presented.HashLegacyValue();

        if (presented.IsRevoked)
        {
            // Its twin request replaced it a moment ago and brought the new cookie: this one tries again with it.
            if (presented.WasRotated && presented.RevokedAt > now - ConcurrentRenewalWindow)
            {
                await _context.SaveChangesAsync(cancellationToken);
                return Result.Failure<AuthResponse>(ConcurrentRenewalError);
            }

            if (presented.WasRotated)
            {
                await UserSessions.EndSessionAsync(
                    _context, presented.SessionId, RefreshTokenRevocationReason.ReuseDetected, now, cancellationToken);
                _logger.LogWarning(
                    "A replaced refresh token was presented again: session {SessionId} of user {UserId} ended.",
                    presented.SessionId,
                    presented.UserId);
            }
            else
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Result.Failure<AuthResponse>(InvalidSessionError);
        }

        if (presented.ExpiresAt <= now)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthResponse>(InvalidSessionError);
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == presented.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Usuario no encontrado o inactivo.");

        var tenantMembership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .Where(x => x.UserId == user.Id && x.Status == TenantUserStatus.Active)
            .OrderByDescending(x => x.TenantId == user.DefaultTenantId)
            .ThenBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantMembership is null && user.DefaultTenantId.HasValue)
        {
            var hasAnyMembershipForTenant = await _context.TenantUsers
                .IgnoreQueryFilters()
                .AnyAsync(x => x.UserId == user.Id && x.TenantId == user.DefaultTenantId.Value, cancellationToken);

            if (!hasAnyMembershipForTenant)
            {
                var role = user.Role == UserRole.TenantAdmin
                    ? TenantUserRole.Admin
                    : TenantUserRole.Staff;

                tenantMembership = new TenantUser(user.DefaultTenantId.Value, user.Id, role, TenantUserStatus.Active);
                _context.TenantUsers.Add(tenantMembership);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        var permissions = tenantMembership is null
            ? []
            : await _tenantAccessService.GetPermissionsAsync(user.Id, tenantMembership.TenantId, cancellationToken);

        var tenantContext = tenantMembership is null
            ? null
            : new TenantTokenContext(
                tenantMembership.TenantId,
                tenantMembership.Role.ToString().ToUpperInvariant(),
                permissions);

        if (user.DefaultTenantId is null && tenantMembership is not null)
            user.DefaultTenantId = tenantMembership.TenantId;

        // Same session, next token: the one presented is replaced and cannot renew it again.
        var accessToken = _jwtTokenService.GenerateAccessToken(user, presented.SessionId, tenantContext);
        var next = _jwtTokenService.GenerateRefreshToken(user.Id, presented.SessionId);
        presented.Revoke(RefreshTokenRevocationReason.Rotated, now);
        _context.RefreshTokens.Add(next.Token);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request renewed with the same token first; its answer carries the session's new token.
            return Result.Failure<AuthResponse>(ConcurrentRenewalError);
        }

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = next.Value,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role.ToString(),
                TenantId = tenantMembership?.TenantId ?? user.DefaultTenantId,
                IsActive = user.IsActive
            }
        });
    }
}
