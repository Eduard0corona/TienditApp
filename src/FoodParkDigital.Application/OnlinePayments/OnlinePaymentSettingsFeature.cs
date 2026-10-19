using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.OnlinePayments;

/// <summary>Configuración › Pagos en línea (Fase 24I): the business's Mercado Pago account and whether the menu offers it.</summary>
public class OnlinePaymentSettingsDto
{
    /// <summary>Whether the platform configured Mercado Pago; without it nothing else applies.</summary>
    public bool Available { get; set; }
    /// <summary>The platform works with Mercado Pago's test accounts: nothing charged is real money.</summary>
    public bool Sandbox { get; set; }
    public bool Connected { get; set; }
    /// <summary>The account's number in Mercado Pago.</summary>
    public long? AccountId { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    /// <summary>When the key expires; it is renewed before, and only an expired one asks to connect again.</summary>
    public DateTimeOffset? KeyExpiresAt { get; set; }
    /// <summary>Connected, but its key no longer works: connect the account again.</summary>
    public bool NeedsReconnect { get; set; }
    public bool AcceptsPayments { get; set; }
    /// <summary>Whether the menu takes orders now; paying online applies to them only.</summary>
    public bool MenuTakesOrders { get; set; }
    /// <summary>Payments in course: the account cannot change until they end (about 45 minutes at most).</summary>
    public int CheckoutsInCourse { get; set; }
    /// <summary>Money still going back to customers through Mercado Pago.</summary>
    public int RefundsInCourse { get; set; }
    /// <summary>Refunds Mercado Pago refused: the business gives that money back from its account.</summary>
    public int RefundsRefused { get; set; }
}

public class OnlinePaymentConnectionStartDto
{
    /// <summary>Mercado Pago's page where the owner authorizes TienditApp; it comes back to Configuración › Pagos en línea.</summary>
    public string AuthorizationUrl { get; set; } = null!;
}

/// <summary>The business's online payments settings (owners and admins).</summary>
[RequiresTenantManager]
public record GetOnlinePaymentSettingsQuery : IRequest<OnlinePaymentSettingsDto>;

public class GetOnlinePaymentSettingsQueryHandler : IRequestHandler<GetOnlinePaymentSettingsQuery, OnlinePaymentSettingsDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;

    public GetOnlinePaymentSettingsQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
    }

    public Task<OnlinePaymentSettingsDto> Handle(GetOnlinePaymentSettingsQuery request, CancellationToken cancellationToken)
        => OnlinePaymentSettings.ReadAsync(_context, _tenantService.TenantId, _gateway, _protector, _clock.UtcNow, cancellationToken);
}

/// <summary>
/// Starts connecting the business's Mercado Pago account (owners and admins): the owner authorizes TienditApp's application
/// in Mercado Pago, with PKCE, and comes back with a code that only this business and this person can use, once, for ten
/// minutes. The same account connects again at any time; another one only with paying online off and nothing in course.
/// </summary>
[RequiresTenantManager]
public record StartOnlinePaymentConnectionCommand : IRequest<Result<OnlinePaymentConnectionStartDto>>;

public class StartOnlinePaymentConnectionCommandHandler
    : IRequestHandler<StartOnlinePaymentConnectionCommand, Result<OnlinePaymentConnectionStartDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly IDateTimeProvider _clock;

    public StartOnlinePaymentConnectionCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _gateway = gateway;
        _clock = clock;
    }

    public async Task<Result<OnlinePaymentConnectionStartDto>> Handle(
        StartOnlinePaymentConnectionCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<OnlinePaymentConnectionStartDto>(OnlinePaymentSettings.NotAvailableMessage);
        if (!Guid.TryParse(_currentUser.UserId, out var userId))
            return Result.Failure<OnlinePaymentConnectionStartDto>(OnlinePaymentSettings.NotAvailableMessage);
        // Connecting the same account again is how a rejected key comes back, also with payments in course: whether it may
        // change to another account is decided when Mercado Pago says which one it is.

        // Earlier attempts of this person, and any that expired, are no longer needed.
        var now = _clock.UtcNow;
        var stale = await _context.OnlinePaymentAuthorizations
            .Where(a => a.UserId == userId || a.ExpiresAt <= now)
            .ToListAsync(cancellationToken);
        _context.OnlinePaymentAuthorizations.RemoveRange(stale);

        // RFC 7636: a verifier of 43 to 128 characters and its S256 challenge.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorization = OnlinePaymentAuthorization.Start(_tenantService.TenantId, userId, verifier, now);
        _context.OnlinePaymentAuthorizations.Add(authorization);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new OnlinePaymentConnectionStartDto
        {
            AuthorizationUrl = _gateway.AuthorizationUrl(authorization.Id, challenge).ToString()
        });
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Finishes connecting the account with the code Mercado Pago sent back (owners and admins): only the person who started
/// it, in the same business, within ten minutes and once. The keys are encrypted before they are stored. A new account
/// keeps paying online off until the business turns it on; connecting again keeps the choice.
/// </summary>
[RequiresTenantManager]
public record CompleteOnlinePaymentConnectionCommand(Guid State, string Code) : IRequest<Result<OnlinePaymentSettingsDto>>;

public class CompleteOnlinePaymentConnectionCommandValidator : AbstractValidator<CompleteOnlinePaymentConnectionCommand>
{
    public CompleteOnlinePaymentConnectionCommandValidator()
    {
        RuleFor(x => x.State).NotEmpty().WithMessage(OnlinePaymentSettings.AuthorizationExpiredMessage);
        RuleFor(x => x.Code).NotEmpty().WithMessage(OnlinePaymentSettings.AuthorizationExpiredMessage).MaximumLength(512);
    }
}

public class CompleteOnlinePaymentConnectionCommandHandler
    : IRequestHandler<CompleteOnlinePaymentConnectionCommand, Result<OnlinePaymentSettingsDto>>
{
    public const string TestAccountMessage =
        "Conectaste una cuenta de prueba de Mercado Pago: conecta la cuenta real del negocio.";
    public const string LiveAccountInSandboxMessage =
        "TienditApp está en modo de pruebas: conecta una cuenta de prueba de Mercado Pago.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;

    public CompleteOnlinePaymentConnectionCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<OnlinePaymentSettingsDto>> Handle(
        CompleteOnlinePaymentConnectionCommand request, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return Result.Failure<OnlinePaymentSettingsDto>(OnlinePaymentSettings.NotAvailableMessage);

        // Only this business's attempts are visible, and only the person who started it may finish it.
        var userId = Guid.TryParse(_currentUser.UserId, out var parsed) ? parsed : Guid.Empty;
        var authorization = await _context.OnlinePaymentAuthorizations
            .FirstOrDefaultAsync(a => a.Id == request.State && a.UserId == userId, cancellationToken);
        if (authorization is null)
            return Result.Failure<OnlinePaymentSettingsDto>(OnlinePaymentSettings.AuthorizationExpiredMessage);

        var now = _clock.UtcNow;
        if (authorization.IsExpired(now))
            return await EndAsync(authorization, OnlinePaymentSettings.AuthorizationExpiredMessage, cancellationToken);

        OnlinePaymentCredentials credentials;
        try
        {
            credentials = await _gateway.ConnectAsync(request.Code.Trim(), authorization.CodeVerifier, cancellationToken);
        }
        catch (OnlinePaymentGatewayRefusedException ex)
        {
            return await EndAsync(authorization, ex.Message, cancellationToken);
        }
        catch (OnlinePaymentGatewayUnavailableException ex)
        {
            // The code still works for a few minutes: the attempt stays, to try again.
            return Result.Failure<OnlinePaymentSettingsDto>(ex.Message);
        }

        // A test account takes no real money, and real money is not taken in the test mode.
        if (credentials.LiveMode == _gateway.Sandbox)
            return await EndAsync(
                authorization, _gateway.Sandbox ? LiveAccountInSandboxMessage : TestAccountMessage, cancellationToken);

        var tenantId = _tenantService.TenantId;
        var access = OnlinePaymentKeys.ProtectAccess(_protector, tenantId, credentials.AccessToken);
        var refresh = OnlinePaymentKeys.ProtectRefresh(_protector, tenantId, credentials.RefreshToken);
        var account = await _context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        var previous = account?.ProviderUserId;
        // Another account: its checkouts and refunds need the account they started with, and none may start meanwhile.
        if (account is not null && account.ProviderUserId != credentials.ProviderUserId)
        {
            if (account.AcceptsPayments)
                return await EndAsync(authorization, OnlinePaymentSettings.TurnOffFirstMessage, cancellationToken);
            if (await OnlinePaymentSettings.InCourseAsync(_context, cancellationToken))
                return await EndAsync(authorization, OnlinePaymentSettings.InCourseMessage, cancellationToken);
        }

        if (account is null)
        {
            account = OnlinePaymentAccount.Connect(
                tenantId, credentials.ProviderUserId, access, refresh, credentials.AccessTokenExpiresAt, credentials.LiveMode, userId, now);
            _context.OnlinePaymentAccounts.Add(account);
        }
        else
        {
            account.Reconnect(credentials.ProviderUserId, access, refresh, credentials.AccessTokenExpiresAt, credentials.LiveMode, userId, now);
        }

        _context.OnlinePaymentAuthorizations.Remove(authorization);
        OnlinePaymentSettings.Audit(_context, tenantId, _currentUser, "OnlinePaymentAccountConnected",
            new { AccountId = credentials.ProviderUserId, Previous = previous });
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await OnlinePaymentSettings.ReadAsync(_context, tenantId, _gateway, _protector, now, cancellationToken));
    }

    /// <summary>The attempt ends (it is used once) and the owner reads why.</summary>
    private async Task<Result<OnlinePaymentSettingsDto>> EndAsync(
        OnlinePaymentAuthorization authorization, string message, CancellationToken cancellationToken)
    {
        _context.OnlinePaymentAuthorizations.Remove(authorization);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Failure<OnlinePaymentSettingsDto>(message);
    }
}

/// <summary>
/// Disconnects the business's Mercado Pago account (owners and admins): its keys are deleted and the menu stops offering
/// to pay online. Only once paying online is off, and not while payments are in course or money is going back through it.
/// </summary>
[RequiresTenantManager]
public record DisconnectOnlinePaymentAccountCommand : IRequest<Result<OnlinePaymentSettingsDto>>;

public class DisconnectOnlinePaymentAccountCommandHandler
    : IRequestHandler<DisconnectOnlinePaymentAccountCommand, Result<OnlinePaymentSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;

    public DisconnectOnlinePaymentAccountCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<OnlinePaymentSettingsDto>> Handle(
        DisconnectOnlinePaymentAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await _context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null)
            return Result.Failure<OnlinePaymentSettingsDto>("El negocio no tiene una cuenta de Mercado Pago conectada.");
        // Off first, so no checkout starts with the account while it goes.
        if (account.AcceptsPayments)
            return Result.Failure<OnlinePaymentSettingsDto>(OnlinePaymentSettings.TurnOffFirstMessage);
        if (await OnlinePaymentSettings.InCourseAsync(_context, cancellationToken))
            return Result.Failure<OnlinePaymentSettingsDto>(OnlinePaymentSettings.InCourseMessage);

        _context.OnlinePaymentAccounts.Remove(account);
        OnlinePaymentSettings.Audit(_context, account.TenantId, _currentUser, "OnlinePaymentAccountDisconnected",
            new { AccountId = account.ProviderUserId });
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(await OnlinePaymentSettings.ReadAsync(
            _context, _tenantService.TenantId, _gateway, _protector, _clock.UtcNow, cancellationToken));
    }
}

/// <summary>Turns paying online on or off in the online menu (owners and admins); on needs a connected account that works.</summary>
[RequiresTenantManager]
public record SetOnlinePaymentsAcceptedCommand(bool Accepts) : IRequest<Result<OnlinePaymentSettingsDto>>;

public class SetOnlinePaymentsAcceptedCommandHandler
    : IRequestHandler<SetOnlinePaymentsAcceptedCommand, Result<OnlinePaymentSettingsDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly ISecretProtector _protector;
    private readonly IDateTimeProvider _clock;

    public SetOnlinePaymentsAcceptedCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUser,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _gateway = gateway;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<OnlinePaymentSettingsDto>> Handle(
        SetOnlinePaymentsAcceptedCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var account = await _context.OnlinePaymentAccounts.FirstOrDefaultAsync(cancellationToken);
        if (account is null)
            return Result.Failure<OnlinePaymentSettingsDto>("Conecta primero la cuenta de Mercado Pago del negocio.");
        if (request.Accepts && (!_gateway.IsConfigured || OnlinePaymentKeys.Access(_protector, account, now) is null))
            return Result.Failure<OnlinePaymentSettingsDto>(OnlinePaymentSettings.ReconnectMessage);

        if (account.AcceptsPayments != request.Accepts)
        {
            account.SetAcceptsPayments(request.Accepts);
            OnlinePaymentSettings.Audit(_context, account.TenantId, _currentUser, "OnlinePaymentsAcceptedChanged",
                new { request.Accepts });
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await OnlinePaymentSettings.ReadAsync(
            _context, _tenantService.TenantId, _gateway, _protector, now, cancellationToken));
    }
}

internal static class OnlinePaymentSettings
{
    public const string NotAvailableMessage = "El pago en línea no está disponible en TienditApp por ahora.";
    public const string AuthorizationExpiredMessage = "La autorización de Mercado Pago ya no es válida. Vuelve a conectar la cuenta.";
    public const string InCourseMessage =
        "Hay pagos o devoluciones en línea en curso: espera a que terminen (unos 45 minutos) para cambiar o desconectar la cuenta.";
    public const string TurnOffFirstMessage = "Apaga el pago en línea antes de desconectar o cambiar la cuenta de Mercado Pago.";
    public const string ReconnectMessage = "La cuenta de Mercado Pago ya no responde: vuelve a conectarla.";

    /// <summary>Payments in course or money going back: they need the account they started with.</summary>
    public static async Task<bool> InCourseAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => await context.OnlineCheckouts.AnyAsync(c => c.Status == OnlineCheckoutStatus.AwaitingPayment, cancellationToken)
            || await context.OnlinePaymentRefunds.AnyAsync(r => r.Status == OnlinePaymentRefundStatus.Requested, cancellationToken);

    public static void Audit(IApplicationDbContext context, Guid tenantId, ICurrentUserService currentUser, string action, object metadata)
        => context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: nameof(OnlinePaymentAccount),
            entityId: tenantId.ToString(),
            action: action,
            performedBy: currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(metadata)));

    public static async Task<OnlinePaymentSettingsDto> ReadAsync(
        IApplicationDbContext context,
        Guid tenantId,
        IOnlinePaymentGateway gateway,
        ISecretProtector protector,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = await context.OnlinePaymentAccounts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var mode = await context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => (OnlineMenuMode?)s.OnlineMenuMode)
            .FirstOrDefaultAsync(cancellationToken);
        return new OnlinePaymentSettingsDto
        {
            Available = gateway.IsConfigured,
            Sandbox = gateway.Sandbox,
            Connected = account is not null,
            AccountId = account?.ProviderUserId,
            ConnectedAt = account?.ConnectedAt,
            KeyExpiresAt = account?.AccessTokenExpiresAt,
            NeedsReconnect = account is not null && OnlinePaymentKeys.Access(protector, account, now) is null,
            AcceptsPayments = account?.AcceptsPayments ?? false,
            MenuTakesOrders = mode == OnlineMenuMode.Orders,
            CheckoutsInCourse = await context.OnlineCheckouts.CountAsync(c => c.Status == OnlineCheckoutStatus.AwaitingPayment, cancellationToken),
            RefundsInCourse = await context.OnlinePaymentRefunds.CountAsync(r => r.Status == OnlinePaymentRefundStatus.Requested, cancellationToken),
            RefundsRefused = await context.OnlinePaymentRefunds.CountAsync(r => r.Status == OnlinePaymentRefundStatus.Failed, cancellationToken)
        };
    }
}
