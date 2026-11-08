using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Employees;

/// <summary>
/// The notices by email that whoever runs the team chooses for each person (Fase 29Q, ADR 0071): the agenda's (each request
/// online and each appointment its customer cancels) and the answers to quotes. The person still changes them in Mi perfil, and
/// the last choice holds. Nobody chooses the owner's from the team: the owner does, in Mi perfil.
/// </summary>
public static class EmployeeNoticeEmails
{
    public const string OwnerChoosesMessage = "El dueño elige sus avisos por correo en Mi perfil.";

    /// <summary>Why <paramref name="member"/>'s notices cannot change as asked: only an owner's, and only if they would change.</summary>
    public static string? RefusalFor(TenantUser member, bool? requestEmails, bool? quoteReplyEmails)
        => member.Role == TenantUserRole.Owner && Changes(member, requestEmails, quoteReplyEmails) ? OwnerChoosesMessage : null;

    /// <summary>
    /// Saves only what changes what the person gets: null leaves a notice as it is, and asking for what they already get keeps
    /// their choice (or its absence) as it was.
    /// </summary>
    /// <returns>What changed, each notice null when it did not (Fase 30A): what the person is told.</returns>
    public static (bool? RequestEmails, bool? QuoteReplyEmails) Apply(TenantUser member, bool? requestEmails, bool? quoteReplyEmails)
    {
        bool? requestsChanged = null, quoteRepliesChanged = null;
        if (requestEmails is { } requests && requests != member.GetsRequestEmails)
        {
            member.ChooseRequestEmails(requests);
            requestsChanged = requests;
        }
        if (quoteReplyEmails is { } quoteReplies && quoteReplies != member.GetsQuoteReplyEmails)
        {
            member.ChooseQuoteReplyEmails(quoteReplies);
            quoteRepliesChanged = quoteReplies;
        }
        return (requestsChanged, quoteRepliesChanged);
    }

    private static bool Changes(TenantUser member, bool? requestEmails, bool? quoteReplyEmails)
        => (requestEmails is { } requests && requests != member.GetsRequestEmails)
            || (quoteReplyEmails is { } quoteReplies && quoteReplies != member.GetsQuoteReplyEmails);
}
