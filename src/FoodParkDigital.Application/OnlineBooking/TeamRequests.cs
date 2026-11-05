using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlineBooking;

/// <summary>
/// Whether a request online comes from the business's own team (Fase 29M, ADR 0067): someone signed in as one of its members
/// (invited or active), or the phone of one of them or of the business (its contact phone, a branch's or its WhatsApp number).
/// Explorar does not count how soon those are answered. Read in the business of the request, which its public page already
/// set; the phones are compared as numbers (E.164, a Mexican mobile with or without the 1 of WhatsApp), however they were
/// written.
/// </summary>
internal static class TeamRequests
{
    public static async Task<bool> FromTeamAsync(
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        Guid tenantId,
        string phone,
        string? signedInUserId,
        CancellationToken cancellationToken)
    {
        var members = context.TenantUsers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.Status != TenantUserStatus.Disabled);
        if (Guid.TryParse(signedInUserId, out var userId) && await members.AnyAsync(m => m.UserId == userId, cancellationToken))
            return true;

        var written = await context.Users
            .AsNoTracking()
            .Where(u => u.PhoneNumber != null && members.Any(m => m.UserId == u.Id))
            .Select(u => u.PhoneNumber)
            .ToListAsync(cancellationToken);
        written.AddRange(await context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId && t.ContactPhone != null)
            .Select(t => t.ContactPhone)
            .ToListAsync(cancellationToken));
        written.AddRange(await context.Branches
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && b.ContactPhone != null)
            .Select(b => b.ContactPhone)
            .ToListAsync(cancellationToken));
        written.AddRange(await context.WhatsAppConfigs
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DisplayPhoneNumber != null)
            .Select(c => c.DisplayPhoneNumber)
            .ToListAsync(cancellationToken));
        var customer = MessagingPhone.ToKey(phone);
        return written.Any(raw => phones.TryNormalizeToE164(raw, CustomerPhoneOwnershipRules.DefaultCountry, out var number)
            && MessagingPhone.ToKey(number) == customer);
    }
}
