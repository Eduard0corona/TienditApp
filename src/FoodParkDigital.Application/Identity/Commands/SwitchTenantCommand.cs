using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[RequiresAuthentication]
public record SwitchTenantCommand(Guid TenantId) : IRequest<Result<AuthResponse>>
{
    /// <summary>
    /// Refresh token of the session being switched, taken by the controller from the HttpOnly cookie
    /// (never from the request body). It is revoked once the new one is issued.
    /// </summary>
    [JsonIgnore]
    public string? CurrentRefreshToken { get; init; }
}

public class SwitchTenantCommandValidator : AbstractValidator<SwitchTenantCommand>
{
    public SwitchTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
    }
}

public class SwitchTenantCommandHandler : IRequestHandler<SwitchTenantCommand, Result<AuthResponse>>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITenantAccessService _tenantAccessService;
    private readonly IDateTimeProvider _clock;

    public SwitchTenantCommandHandler(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        ITenantAccessService tenantAccessService,
        IDateTimeProvider clock)
    {
        _currentUserService = currentUserService;
        _context = context;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _tenantAccessService = tenantAccessService;
        _clock = clock;
    }

    public async Task<Result<AuthResponse>> Handle(SwitchTenantCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure<AuthResponse>("Usuario no autenticado.");

        var membership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                    && x.TenantId == request.TenantId
                    && x.Status == TenantUserStatus.Active,
                cancellationToken);
        if (membership is null)
            return Result.Failure<AuthResponse>("No tienes acceso activo a este negocio.");

        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Usuario no encontrado o inactivo.");

        user.DefaultTenantId = request.TenantId;
        await _context.SaveChangesAsync(cancellationToken);

        var permissions = await _tenantAccessService.GetPermissionsAsync(user.Id, request.TenantId, cancellationToken);
        var tenantRole = membership.Role.ToString().ToUpperInvariant();

        // The session goes on in the other business: its token is replaced, as in a renewal (the previous one stayed
        // valid after a switch and could start a parallel session). Without it, the switch starts a new session.
        var current = await UserSessions.FindAsync(_context, request.CurrentRefreshToken, cancellationToken);
        if (current is not null && (current.UserId != user.Id || !current.IsActive))
            current = null;
        var sessionId = current?.SessionId ?? Guid.NewGuid();
        current?.Revoke(RefreshTokenRevocationReason.Rotated, _clock.UtcNow);

        var accessToken = _jwtTokenService.GenerateAccessToken(
            user,
            sessionId,
            new TenantTokenContext(request.TenantId, tenantRole, permissions));
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id, sessionId);
        _context.RefreshTokens.Add(refreshToken.Token);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A renewal replaced the session's token at the same moment.
            return Result.Failure<AuthResponse>(RefreshTokenCommandHandler.ConcurrentRenewalError);
        }

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Value,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
            RequiresTwoFactor = false,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                Role = user.Role.ToString(),
                TenantId = user.DefaultTenantId,
                IsActive = user.IsActive,
                AvatarUrl = string.IsNullOrEmpty(user.AvatarUrl) ? null : $"{user.AvatarUrl}_md.webp",
                AvatarImages = AvatarImageUrlsDto.FromBaseUrl(user.AvatarUrl)
            }
        });
    }
}
