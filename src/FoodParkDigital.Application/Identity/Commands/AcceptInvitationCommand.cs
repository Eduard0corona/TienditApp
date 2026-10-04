using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>What the invitation screen shows before the person chooses a password.</summary>
public class InvitationDto
{
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public List<string> Businesses { get; set; } = [];
}

[AllowAnonymousRequest("Acceso público: aceptar una invitación al equipo con el enlace recibido.")]
public record GetInvitationQuery(Guid UserId, string Token) : IRequest<Result<InvitationDto>>;

public class GetInvitationQueryHandler : IRequestHandler<GetInvitationQuery, Result<InvitationDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;

    public GetInvitationQueryHandler(UserManager<ApplicationUser> userManager, IApplicationDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<Result<InvitationDto>> Handle(GetInvitationQuery request, CancellationToken cancellationToken)
    {
        var invitation = await Invitation.CheckAsync(_userManager, _context, request.UserId, request.Token, cancellationToken);
        if (invitation.IsFailure)
            return Result.Failure<InvitationDto>(invitation.Error!);

        var (user, businesses) = invitation.Value;
        return Result.Success(new InvitationDto
        {
            // The person sees the email they will sign in with and can tell the admin if it is not theirs.
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            Businesses = businesses
        });
    }
}

/// <summary>
/// The invited person chooses their password and enters: every pending invitation of the account becomes active and
/// the session starts right away.
/// </summary>
[AllowAnonymousRequest("Acceso público: aceptar una invitación al equipo con el enlace recibido.")]
public record AcceptInvitationCommand : IRequest<Result<AuthResponse>>
{
    public Guid UserId { get; init; }
    public string Token { get; init; } = null!;
    public string NewPassword { get; init; } = null!;
}

public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
    }
}

public class AcceptInvitationCommandHandler : IRequestHandler<AcceptInvitationCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IAuthSessionIssuer _authSessionIssuer;

    public AcceptInvitationCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IAuthSessionIssuer authSessionIssuer)
    {
        _userManager = userManager;
        _context = context;
        _authSessionIssuer = authSessionIssuer;
    }

    public async Task<Result<AuthResponse>> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        var invitation = await Invitation.CheckAsync(_userManager, _context, request.UserId, request.Token, cancellationToken);
        if (invitation.IsFailure)
            return Result.Failure<AuthResponse>(invitation.Error!);

        var user = invitation.Value.User;
        var policy = await _userManager.ValidatePasswordPolicyAsync(user, request.NewPassword);
        if (!policy.Succeeded)
            return Result.Failure<AuthResponse>(policy.ToErrorMessage());

        // The link reached the person by the email of the account, or from the admin who wrote that email (and the
        // screen showed it): it confirms the email the person signs in with.
        user.EmailConfirmed = true;
        await EmployeeInvitations.ActivatePendingAsync(_context, user.Id, cancellationToken);

        // One save: the password (which changes the security stamp and so ends the link), the email and the memberships.
        var added = await _userManager.AddPasswordAsync(user, request.NewPassword);
        if (!added.Succeeded)
            return Result.Failure<AuthResponse>(added.ToErrorMessage());

        return Result.Success(await _authSessionIssuer.IssueAsync(user, cancellationToken));
    }
}

internal static class Invitation
{
    /// <summary>The account of a valid, pending invitation and the businesses that wait for it.</summary>
    public static async Task<Result<(ApplicationUser User, List<string> Businesses)>> CheckAsync(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        Guid userId,
        string token,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: the person has no business selected before accepting; filtered by the account.
        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure<(ApplicationUser, List<string>)>(EmployeeInvitations.InvalidMessage);
        if (await userManager.HasPasswordAsync(user))
            return Result.Failure<(ApplicationUser, List<string>)>(EmployeeInvitations.AlreadyAcceptedMessage);
        if (string.IsNullOrWhiteSpace(token) || !await EmployeeInvitations.IsValidAsync(userManager, user, token))
            return Result.Failure<(ApplicationUser, List<string>)>(EmployeeInvitations.InvalidMessage);

        // An invitation the business cancelled (the person disabled) no longer opens anything.
        var businesses = await EmployeeInvitations.PendingBusinessesAsync(context, user.Id, cancellationToken);
        return businesses.Count == 0
            ? Result.Failure<(ApplicationUser, List<string>)>(EmployeeInvitations.InvalidMessage)
            : Result.Success((user, businesses));
    }
}
