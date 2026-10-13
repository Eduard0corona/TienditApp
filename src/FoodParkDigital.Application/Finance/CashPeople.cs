using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Finance;

/// <summary>
/// The names of the people a cash record keeps by their user (the email they signed in with), for the cuts and screens
/// that show a name instead of the email. Memberships are filtered by business: only a member of the business in scope
/// is found, so a user of another business, a removed member or a value no person signed ("Sistema", "system-migration")
/// has no name and keeps its user. Neither has a name two members share: their users tell them apart, as before.
/// </summary>
internal static class CashPeople
{
    // Stored when no person did it: no user in the request, or rows from before per-cashier drawers.
    private static readonly HashSet<string> NotPeople = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sistema",
        "system",
        "system-migration"
    };

    /// <summary>The names of every user and user id of a response, in one query.</summary>
    public static async Task<CashPeopleNames> ResolveAsync(
        IApplicationDbContext context,
        IEnumerable<string?> users,
        IEnumerable<Guid?> userIds,
        CancellationToken cancellationToken)
    {
        var emails = users.Where(IsPerson).Select(user => Normalize(user!)).Distinct().ToList();
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        if (emails.Count == 0 && ids.Count == 0)
            return CashPeopleNames.None;

        var members = Members(context);
        var found = await members
            .Where(u => ids.Contains(u.Id) || (u.NormalizedEmail != null && emails.Contains(u.NormalizedEmail)))
            .Select(u => new
            {
                u.Id,
                u.NormalizedEmail,
                u.FirstName,
                u.LastName,
                Namesakes = members.Count(o => o.FirstName == u.FirstName && o.LastName == u.LastName)
            })
            .ToListAsync(cancellationToken);

        var byUserId = new Dictionary<Guid, string>();
        var byUser = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in found)
        {
            if (member.Namesakes > 1 || FullName(member.FirstName, member.LastName) is not { } name)
                continue;
            byUserId[member.Id] = name;
            if (member.NormalizedEmail is { } email)
                byUser[email] = name;
        }

        return new CashPeopleNames(byUserId, byUser);
    }

    /// <summary>
    /// The user of the one member of the business called <paramref name="name"/>, whatever its case; null when no member
    /// or more than one is called so (then only the user itself finds their records).
    /// </summary>
    public static async Task<string?> UserCalledAsync(IApplicationDbContext context, string name, CancellationToken cancellationToken)
    {
        var wanted = name.Trim().ToUpperInvariant();
        if (wanted.Length == 0)
            return null;

        var users = await Members(context)
            .Where(u => (u.FirstName.Trim() + " " + u.LastName.Trim()).Trim().ToUpper() == wanted)
            .Select(u => u.Email)
            .Take(2)
            .ToListAsync(cancellationToken);
        return users.Count == 1 ? users[0] : null;
    }

    internal static bool IsPerson(string? user) => !string.IsNullOrWhiteSpace(user) && !NotPeople.Contains(user.Trim());

    // As Identity normalizes the email it looks accounts up by.
    internal static string Normalize(string user) => user.Trim().ToUpperInvariant();

    private static string? FullName(string? firstName, string? lastName)
    {
        var name = $"{firstName?.Trim()} {lastName?.Trim()}".Trim();
        return name.Length == 0 ? null : name;
    }

    // Users are not filtered by business; their memberships are, so a user without one in this business is not found.
    private static IQueryable<ApplicationUser> Members(IApplicationDbContext context) => context.TenantUsers
        .AsNoTracking()
        .Join(context.Users.AsNoTracking(), m => m.UserId, u => u.Id, (m, u) => u);
}

/// <summary>The names <see cref="CashPeople.ResolveAsync"/> found; a person it did not find keeps their user.</summary>
internal sealed class CashPeopleNames
{
    public static readonly CashPeopleNames None = new(new Dictionary<Guid, string>(), new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<Guid, string> _byUserId;
    private readonly IReadOnlyDictionary<string, string> _byUser;

    public CashPeopleNames(IReadOnlyDictionary<Guid, string> byUserId, IReadOnlyDictionary<string, string> byUser)
    {
        _byUserId = byUserId;
        _byUser = byUser;
    }

    /// <summary>The name of the person, by their user id when the record keeps it, else by their user; null when none.</summary>
    public string? NameOf(string? user, Guid? userId = null)
    {
        if (userId is { } id && _byUserId.TryGetValue(id, out var byId))
            return byId;
        return CashPeople.IsPerson(user) && _byUser.TryGetValue(CashPeople.Normalize(user!), out var byUser) ? byUser : null;
    }
}
