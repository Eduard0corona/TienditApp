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
    public static string? RefusalFor(TenantUser member, NoticeChoices asked)
        => member.Role == TenantUserRole.Owner && Changes(member, asked).Any ? OwnerChoosesMessage : null;

    /// <summary>
    /// Saves only what changes what the person gets: null leaves a notice as it is, and asking for what they already get keeps
    /// their choice (or its absence) as it was.
    /// </summary>
    /// <returns>What changed, each notice null when it did not (Fase 30A): what the person is told.</returns>
    public static NoticeChoices Apply(TenantUser member, NoticeChoices asked)
    {
        var changed = Changes(member, asked);
        if (changed.RequestEmails is { } requests)
            member.ChooseRequestEmails(requests);
        if (changed.QuoteReplyEmails is { } quoteReplies)
            member.ChooseQuoteReplyEmails(quoteReplies);
        if (changed.OrderCancellationEmails is { } orderCancellations)
            member.ChooseOrderCancellationEmails(orderCancellations);
        return changed;
    }

    private static NoticeChoices Changes(TenantUser member, NoticeChoices asked) => new(
        asked.RequestEmails is { } requests && requests != member.GetsRequestEmails ? requests : null,
        asked.QuoteReplyEmails is { } quoteReplies && quoteReplies != member.GetsQuoteReplyEmails ? quoteReplies : null,
        asked.OrderCancellationEmails is { } orders && orders != member.GetsOrderCancellationEmails ? orders : null);
}

/// <summary>
/// The notices by email chosen for a person, each null when left as it is: the agenda's, the answers to quotes and, since the
/// Fase 30B (ADR 0082), the online orders their customers cancel.
/// </summary>
public sealed record NoticeChoices(bool? RequestEmails, bool? QuoteReplyEmails, bool? OrderCancellationEmails = null)
{
    public bool Any => RequestEmails is not null || QuoteReplyEmails is not null || OrderCancellationEmails is not null;
}
