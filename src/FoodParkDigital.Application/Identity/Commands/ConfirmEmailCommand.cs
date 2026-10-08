using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Entities;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Confirms the email of an account from the link sent when it signed up (or a new one asked for later), which
/// activates it: signing in needs a confirmed email or phone. Opening the link again succeeds.
/// </summary>
[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record ConfirmEmailCommand(string UserId, string Token) : IRequest<Result>;

public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, Result>
{
    /// <summary>One answer for an unknown account and a wrong or expired token: the link tells nothing else.</summary>
    public const string InvalidLinkMessage = "El enlace no es válido o ya expiró. Pide uno nuevo.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;

    public ConfirmEmailCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<Result> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.UserId, out var userId) || string.IsNullOrWhiteSpace(request.Token))
            return Result.Failure(InvalidLinkMessage);

        // The account is global (no tenant filter applies to it) and nobody is signed in yet.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure(InvalidLinkMessage);

        if (user.EmailConfirmed)
            return Result.Success();

        var result = await _userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
            return Result.Failure(InvalidLinkMessage);

        return Result.Success();
    }
}
