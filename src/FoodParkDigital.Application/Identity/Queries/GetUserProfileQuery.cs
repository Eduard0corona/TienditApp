using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Queries;

[RequiresAuthentication]
public record GetUserProfileQuery : IRequest<Result<UserProfileDto>>;

public class GetUserProfileQueryHandler : IRequestHandler<GetUserProfileQuery, Result<UserProfileDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;

    public GetUserProfileQueryHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserProfileDto>> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result.Failure<UserProfileDto>("Usuario no autenticado.");

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Result.Failure<UserProfileDto>("Usuario no encontrado.");

        var dto = user.ToProfileDto();
        // Inside a business the role is the one the person has there (the token's), as the API applies it.
        if (!string.IsNullOrEmpty(_currentUserService.TenantRole) && !string.IsNullOrEmpty(_currentUserService.Role))
            dto.Role = _currentUserService.Role;
        dto.TenantRole = _currentUserService.TenantRole;
        dto.Permissions = _currentUserService.Permissions.ToList();
        return Result.Success(dto);
    }
}
