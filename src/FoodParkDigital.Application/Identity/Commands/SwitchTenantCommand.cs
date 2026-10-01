using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

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

    public SwitchTenantCommandHandler(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        ITenantAccessService tenantAccessService)
    {
        _currentUserService = currentUserService;
        _context = context;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _tenantAccessService = tenantAccessService;
    }

    public async Task<Result<AuthResponse>> Handle(SwitchTenantCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure<AuthResponse>("Invalid user context.");

        var membership = await _context.TenantUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.UserId == userId
                    && x.TenantId == request.TenantId
                    && x.Status == TenantUserStatus.Active,
                cancellationToken);
        if (membership is null)
            return Result.Failure<AuthResponse>("No tienes acceso activo a este tenant.");

        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Usuario no encontrado o inactivo.");

        user.DefaultTenantId = request.TenantId;
        await _context.SaveChangesAsync(cancellationToken);

        var permissions = await _tenantAccessService.GetPermissionsAsync(user.Id, request.TenantId, cancellationToken);
        var tenantRole = membership.Role.ToString().ToUpperInvariant();
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user,
            new TenantTokenContext(request.TenantId, tenantRole, permissions));
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id);

        // Rotation: the previous token stayed valid after a switch and could start a parallel session.
        if (!string.IsNullOrEmpty(request.CurrentRefreshToken))
        {
            var previousToken = await _context.RefreshTokens.FirstOrDefaultAsync(
                x => x.Token == request.CurrentRefreshToken && x.UserId == user.Id && x.RevokedAt == null,
                cancellationToken);
            previousToken?.Revoke(refreshToken.Token);
        }

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
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
