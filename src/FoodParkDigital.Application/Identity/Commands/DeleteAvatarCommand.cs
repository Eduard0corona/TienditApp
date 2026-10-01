using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

public record DeleteAvatarCommand : IRequest<Result>;

public class DeleteAvatarCommandHandler : IRequestHandler<DeleteAvatarCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFileStorageService _fileStorageService;

    public DeleteAvatarCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IFileStorageService fileStorageService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _fileStorageService = fileStorageService;
    }

    public async Task<Result> Handle(DeleteAvatarCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Result.Failure("Usuario no autenticado.");

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive)
            return Result.Failure("Usuario no encontrado.");

        if (string.IsNullOrEmpty(user.AvatarUrl))
            return Result.Success();

        await _fileStorageService.DeleteVariantsAsync(
            user.AvatarUrl,
            TenantStorageFolders.Avatars(_tenantService.TenantId),
            cancellationToken);
        user.AvatarUrl = null;
        await _userManager.UpdateAsync(user);

        return Result.Success();
    }
}
