using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

[RequiresAuthentication]
public record DeleteAvatarCommand : IRequest<Result>;

public class DeleteAvatarCommandHandler : IRequestHandler<DeleteAvatarCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;

    public DeleteAvatarCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        IFileStorageService fileStorageService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _context = context;
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

        // The files go only once the account no longer points to them.
        var avatarUrl = user.AvatarUrl;
        user.AvatarUrl = null;
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Result.Failure("No se pudo quitar el avatar.");

        await AvatarFiles.DeleteAsync(_fileStorageService, _context, user.Id, avatarUrl, cancellationToken);
        return Result.Success();
    }
}
