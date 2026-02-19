using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

public record RefreshTokenCommand(string? ExpiredToken, string RefreshToken) : IRequest<Result<AuthResponse>>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITenantAccessService _tenantAccessService;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        ITenantAccessService tenantAccessService)
    {
        _context = context;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _tenantAccessService = tenantAccessService;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        Guid? userId = null;

        // Si hay un JWT expirado, extraer el userId de ahí
        if (!string.IsNullOrEmpty(request.ExpiredToken))
        {
            var principal = _jwtTokenService.GetPrincipalFromExpiredToken(request.ExpiredToken);
            var userIdClaim = principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var parsedId))
                userId = parsedId;
        }

        // Si no se pudo obtener el userId del JWT, buscarlo desde el refresh token
        userId ??= await _jwtTokenService.GetUserIdFromRefreshTokenAsync(request.RefreshToken);

        if (userId is null)
            return Result.Failure<AuthResponse>("Token inválido.");

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthResponse>("Usuario no encontrado o inactivo.");

        // Validar refresh token
        var isValid = await _jwtTokenService.ValidateRefreshTokenAsync(userId.Value, request.RefreshToken);
        if (!isValid)
            return Result.Failure<AuthResponse>("Refresh token inválido o expirado.");

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

        // Generar nuevo par de tokens
        var newAccessToken = _jwtTokenService.GenerateAccessToken(user, tenantContext);
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken(user.Id);

        // Revocar el refresh token anterior
        await _jwtTokenService.RevokeRefreshTokenAsync(request.RefreshToken, newRefreshToken.Token);

        _context.RefreshTokens.Add(newRefreshToken);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
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
