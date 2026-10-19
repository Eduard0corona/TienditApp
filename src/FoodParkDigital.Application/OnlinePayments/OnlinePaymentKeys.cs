using System.Security.Cryptography;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>
/// The keys of a business's Mercado Pago account, encrypted for that business (Fase 24I, ADR 0031): a key copied to another
/// business's row does not decrypt there. A key that no longer decrypts (the platform changed its encryption key) reads as
/// none, and the owner connects the account again.
/// </summary>
internal static class OnlinePaymentKeys
{
    public static string ProtectAccess(ISecretProtector protector, Guid tenantId, string accessToken)
        => protector.Protect(accessToken, AccessPurpose(tenantId));

    public static string ProtectRefresh(ISecretProtector protector, Guid tenantId, string refreshToken)
        => protector.Protect(refreshToken, RefreshPurpose(tenantId));

    /// <summary>The key payments are made and read with; null when it expired or no longer decrypts.</summary>
    public static string? Access(ISecretProtector protector, OnlinePaymentAccount account, DateTimeOffset now)
        => account.IsUsable(now) ? Unprotect(protector, account.ProtectedAccessToken, AccessPurpose(account.TenantId)) : null;

    /// <summary>The key that renews the access; null when it no longer decrypts.</summary>
    public static string? Refresh(ISecretProtector protector, OnlinePaymentAccount account)
        => Unprotect(protector, account.ProtectedRefreshToken, RefreshPurpose(account.TenantId));

    /// <summary>
    /// Mercado Pago rejected the account's key <paramref name="protectedAccessToken"/> (as the row had it when the call was
    /// made): it counts as expired from now on, so the menu stops offering to pay online, the sweep stops asking with it and
    /// Configuración › Pagos en línea asks to connect it again. A key renewed or connected since stays. Saved only when
    /// <paramref name="save"/> (the queue saves a handler's changes itself).
    /// </summary>
    public static async Task RejectAsync(
        IApplicationDbContext context, string protectedAccessToken, DateTimeOffset now, bool save, CancellationToken cancellationToken)
    {
        var account = await context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null)
            return;

        account.RejectKey(protectedAccessToken, now);
        if (!save)
            return;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The owner connected it again meanwhile: the new key stays.
            context.ChangeTracker.Clear();
        }
    }

    private static string? Unprotect(ISecretProtector protector, string value, string purpose)
    {
        if (!protector.IsConfigured)
            return null;
        try
        {
            return protector.Unprotect(value, purpose);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string AccessPurpose(Guid tenantId) => $"mercadopago-access:{tenantId:N}";

    private static string RefreshPurpose(Guid tenantId) => $"mercadopago-refresh:{tenantId:N}";
}
