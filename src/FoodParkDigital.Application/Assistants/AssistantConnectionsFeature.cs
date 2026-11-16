using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Assistants;

/// <summary>An assistant connected to the account, as Mi cuenta lists it.</summary>
public sealed class MyAssistantConnectionDto
{
    public Guid Id { get; init; }

    /// <summary>The name it gave when it registered; null when it gave none.</summary>
    public string? Name { get; init; }

    /// <summary>The site its permission returned to: the host of the address its code went to, the one the page showed.</summary>
    public string Site { get; init; } = null!;

    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>When it last read the account, within five minutes; null when it never did.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>The assistants connected to the account (Fase 32E, ADR 0097), the newest first; none for an account of a business.</summary>
[RequiresAuthentication]
public record GetMyAssistantConnectionsQuery : IRequest<List<MyAssistantConnectionDto>>;

public class GetMyAssistantConnectionsQueryHandler : IRequestHandler<GetMyAssistantConnectionsQuery, List<MyAssistantConnectionDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetMyAssistantConnectionsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<MyAssistantConnectionDto>> Handle(GetMyAssistantConnectionsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return [];

        var connections = await (
                from connection in _context.AssistantConnections.AsNoTracking()
                where connection.UserId == userId && connection.RevokedAt == null
                join client in _context.AssistantClients.AsNoTracking() on connection.ClientId equals client.Id
                orderby connection.GrantedAt descending
                select new { connection.Id, client.Name, connection.RedirectUri, connection.GrantedAt, connection.LastUsedAt })
            .ToListAsync(cancellationToken);
        return connections
            .Select(c => new MyAssistantConnectionDto
            {
                Id = c.Id,
                Name = c.Name,
                Site = AssistantAuthorization.SiteOf(c.RedirectUri),
                GrantedAt = c.GrantedAt,
                LastUsedAt = c.LastUsedAt
            })
            .ToList();
    }
}

/// <summary>
/// Disconnects an assistant of the account (Fase 32E, ADR 0097): its tokens stop opening the account at its next request.
/// Only one of the account's own; any other is not found.
/// </summary>
[RequiresAuthentication]
public record DisconnectMyAssistantCommand(Guid Id) : IRequest<Result>;

public class DisconnectMyAssistantCommandHandler : IRequestHandler<DisconnectMyAssistantCommand, Result>
{
    public const string NotFoundMessage = "Ese asistente ya no está conectado a tu cuenta.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public DisconnectMyAssistantCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(DisconnectMyAssistantCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure(NotFoundMessage);

        var connection = await _context.AssistantConnections
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.UserId == userId && c.RevokedAt == null, cancellationToken);
        if (connection is null)
            return Result.Failure(NotFoundMessage);

        connection.Revoke(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
