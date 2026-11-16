using FoodParkDigital.Application.Assistants;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The authorization server an AI assistant meets to use a customer's account (Fase 32E, ADR 0097): it registers, the
/// customer allows it from the permission page, it exchanges its code once with PKCE, renews with a refresh token that
/// rotates, and every request of /mcp/cuenta finds who it is. A copied code or refresh token ends the connection; Mi cuenta
/// lists and disconnects them; an account of a business never connects.
/// </summary>
public class AssistantAuthorizationFlowTests
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Resource = "https://app.test/mcp/cuenta";
    // RFC 7636, appendix B.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public async Task Register_ShouldGiveAPublicClient_OrSayWhyNot()
    {
        await using var fixture = SalesTestFixture.Create();

        var registered = await Register(fixture).Handle(new RegisterAssistantClientCommand
        {
            ClientName = "Claude",
            RedirectUris = [Callback],
            TokenEndpointAuthMethod = "none",
            GrantTypes = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"]
        }, CancellationToken.None);

        Assert.Null(registered.Error);
        Assert.True(AssistantSecrets.IsWellFormed(registered.ClientId));
        Assert.Equal(("Claude", Callback), (registered.ClientName, Assert.Single(registered.RedirectUris)));
        Assert.Equal(registered.ClientId, (await fixture.Context.AssistantClients.SingleAsync()).ClientId);

        Assert.Equal(OAuthErrors.InvalidRedirectUri, (await Register(fixture).Handle(
            new RegisterAssistantClientCommand { RedirectUris = ["http://evil.example/cb"] }, CancellationToken.None)).Error);
        Assert.Equal(OAuthErrors.InvalidRedirectUri, (await Register(fixture).Handle(
            new RegisterAssistantClientCommand(), CancellationToken.None)).Error);
        Assert.Equal(OAuthErrors.InvalidClientMetadata, (await Register(fixture).Handle(
            new RegisterAssistantClientCommand { RedirectUris = [Callback], TokenEndpointAuthMethod = "client_secret_basic" }, CancellationToken.None)).Error);
        Assert.Equal(OAuthErrors.InvalidClientMetadata, (await Register(fixture).Handle(
            new RegisterAssistantClientCommand { RedirectUris = [Callback], GrantTypes = ["client_credentials"] }, CancellationToken.None)).Error);
        Assert.Equal(1, await fixture.Context.AssistantClients.CountAsync());
    }

    [Fact]
    public async Task Check_ShouldSayOnThePage_AnUnknownClientOrAddress_AndReturnTheRestToTheAssistant()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback, "http://127.0.0.1:33418/callback");

        var valid = await Check(fixture, Request(clientId));
        Assert.True(valid.Valid);
        Assert.Equal(("Claude", "claude.ai", false), (valid.ClientName, valid.RedirectSite, valid.RedirectsToThisDevice));
        Assert.Null(valid.ErrorRedirectUrl);
        // Without a scope or a resource it is the account and this server; on this device, any port.
        var desktop = await Check(fixture, Request(clientId) with { Scope = null, Resource = null, RedirectUri = "http://127.0.0.1:50000/callback" });
        Assert.Equal((true, "127.0.0.1:50000", true), (desktop.Valid, desktop.RedirectSite, desktop.RedirectsToThisDevice));

        // The client or its address wrong: said here, never sent anywhere.
        var unknown = await Check(fixture, Request(new string('z', 43)));
        Assert.Equal((false, AssistantAuthorization.UnknownClientMessage, (string?)null), (unknown.Valid, unknown.Problem, unknown.ErrorRedirectUrl));
        var foreign = await Check(fixture, Request(clientId) with { RedirectUri = "https://evil.example/cb" });
        Assert.Equal((false, AssistantAuthorization.InvalidRedirectMessage, (string?)null), (foreign.Valid, foreign.Problem, foreign.ErrorRedirectUrl));

        // The rest goes back to the assistant with its error and its state.
        foreach (var (wrong, error) in new (CheckAssistantAuthorizationQuery, string)[]
        {
            (Request(clientId) with { CodeChallenge = null }, OAuthErrors.InvalidRequest),
            (Request(clientId) with { CodeChallengeMethod = "plain" }, OAuthErrors.InvalidRequest),
            (Request(clientId) with { ResponseType = "token" }, OAuthErrors.UnsupportedResponseType),
            (Request(clientId) with { Scope = "cuenta admin" }, OAuthErrors.InvalidScope),
            (Request(clientId) with { Resource = "https://app.test/mcp" }, OAuthErrors.InvalidTarget),
        })
        {
            var check = await Check(fixture, wrong);
            Assert.False(check.Valid);
            Assert.Null(check.Problem);
            Assert.StartsWith($"{Callback}?error={error}&error_description=", check.ErrorRedirectUrl);
            Assert.EndsWith("&state=xyz%20123", check.ErrorRedirectUrl);
        }
    }

    [Fact]
    public async Task Approve_ShouldGiveACodeOfOneUse_ToACustomerAccount_OrReturnAccessDenied()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var account = await CustomerAsync(fixture);

        var denied = await Approve(fixture).Handle(Approval(clientId, allow: false), CancellationToken.None);
        Assert.True(denied.IsSuccess, denied.Error);
        Assert.Equal($"{Callback}?error=access_denied&error_description=El%20cliente%20no%20dio%20permiso.&state=xyz%20123", denied.Value!.RedirectUrl);
        Assert.Empty(await fixture.Context.AssistantAuthorizationCodes.ToListAsync());

        var allowed = await Approve(fixture).Handle(Approval(clientId, allow: true), CancellationToken.None);
        Assert.True(allowed.IsSuccess, allowed.Error);
        var code = CodeOf(allowed.Value!.RedirectUrl);
        Assert.EndsWith("&state=xyz%20123", allowed.Value.RedirectUrl);
        var stored = await fixture.Context.AssistantAuthorizationCodes.SingleAsync();
        // Only its hash, tied to the account, the address, the challenge and this server.
        Assert.Equal(AssistantSecrets.HashOf(code), stored.CodeHash);
        Assert.Equal((account, Callback, Challenge, Resource, "cuenta"), (stored.UserId, stored.RedirectUri, stored.CodeChallenge, stored.Resource, stored.Scope));

        // Checked again: a request changed on the way is refused.
        var tampered = await Approve(fixture).Handle(Approval(clientId, allow: true) with { RedirectUri = "https://evil.example/cb" }, CancellationToken.None);
        Assert.Equal(AssistantAuthorization.InvalidRedirectMessage, tampered.Error);
    }

    [Fact]
    public async Task Approve_ShouldRefuse_AnAccountOfABusiness_AnInactiveOne_AndA21stAssistant()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);

        var owner = await CustomerAsync(fixture, UserRole.TenantAdmin);
        Assert.Equal(AssistantAuthorization.CustomersOnlyMessage,
            (await Approve(fixture).Handle(Approval(clientId, allow: true), CancellationToken.None)).Error);
        var inactive = await CustomerAsync(fixture, active: false);
        Assert.Equal(AssistantAuthorization.CustomersOnlyMessage,
            (await Approve(fixture).Handle(Approval(clientId, allow: true), CancellationToken.None)).Error);

        // Twenty connected: a new one waits until one is disconnected; the same one again replaces itself.
        var account = await CustomerAsync(fixture);
        var others = Enumerable.Range(0, AssistantConnection.MaxActivePerAccount)
            .Select(i => AssistantClient.Register(AssistantAuthorization.NewSecret(), $"Otro {i}", [Callback], SalesTestFixture.Now))
            .ToList();
        fixture.Context.AssistantClients.AddRange(others);
        fixture.Context.AssistantConnections.AddRange(others.Select(c => AssistantConnection.Grant(account, c.Id, "cuenta", SalesTestFixture.Now)));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(AssistantConnection.TooManyMessage,
            (await Approve(fixture).Handle(Approval(clientId, allow: true), CancellationToken.None)).Error);
        Assert.True((await Approve(fixture).Handle(Approval(others[0].ClientId, allow: true), CancellationToken.None)).IsSuccess);
        Assert.NotEqual(owner, inactive);
    }

    [Fact]
    public async Task ExchangeCode_ShouldGiveTokensOnce_WithItsVerifier_AndAGivenCodeAgainShouldEndTheConnection()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var account = await CustomerAsync(fixture);
        var code = await AllowAsync(fixture, clientId);

        // Each thing it was tied to, wrong.
        foreach (var wrong in new[]
        {
            CodeExchange(code, clientId) with { CodeVerifier = Verifier[..^1] + "l" },
            CodeExchange(code, clientId) with { RedirectUri = Callback + "/" },
            CodeExchange(code, new string('z', 43)),
            CodeExchange(new string('q', 43), clientId),
        })
        {
            var refused = await ExchangeAsync(fixture, wrong);
            Assert.Equal(OAuthErrors.InvalidGrant, refused.Error);
        }
        Assert.Equal(OAuthErrors.InvalidTarget, (await ExchangeAsync(fixture, CodeExchange(code, clientId) with { Resource = "https://app.test/mcp" })).Error);
        Assert.Equal(OAuthErrors.UnsupportedGrantType, (await ExchangeAsync(fixture, CodeExchange(code, clientId) with { GrantType = "password" })).Error);

        var tokens = await ExchangeAsync(fixture, CodeExchange(code, clientId));
        Assert.Null(tokens.Error);
        Assert.True(AssistantSecrets.IsWellFormed(tokens.AccessToken));
        Assert.True(AssistantSecrets.IsWellFormed(tokens.RefreshToken));
        Assert.Equal((3600, "cuenta"), (tokens.ExpiresIn, tokens.Scope));
        var connection = await fixture.Context.AssistantConnections.SingleAsync();
        Assert.Equal((account, true), (connection.UserId, connection.IsActive));
        // Only their hashes are stored.
        Assert.Equal(
            new[] { AssistantSecrets.HashOf(tokens.AccessToken!), AssistantSecrets.HashOf(tokens.RefreshToken!) }.Order(),
            (await fixture.Context.AssistantTokens.Select(t => t.TokenHash).ToListAsync()).Order());
        Assert.Equal(account, (await Authenticate(fixture, tokens.AccessToken))!.UserId);

        // The same code again: someone else has it, and the connection it gave stops.
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, CodeExchange(code, clientId))).Error);
        Assert.False((await fixture.Context.AssistantConnections.SingleAsync()).IsActive);
        Assert.Null(await Authenticate(fixture, tokens.AccessToken));
    }

    [Fact]
    public async Task ExchangeCode_ShouldRefuse_AnExpiredCode_AndOneOfAnAccountNoLongerActive()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var account = await CustomerAsync(fixture);

        var late = await AllowAsync(fixture, clientId);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, CodeExchange(late, clientId))).Error);

        var code = await AllowAsync(fixture, clientId);
        (await fixture.Context.Users.SingleAsync(u => u.Id == account)).IsActive = false;
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, CodeExchange(code, clientId))).Error);
        Assert.Empty(await fixture.Context.AssistantConnections.ToListAsync());
    }

    [Fact]
    public async Task AllowingTheSameAssistantAgain_ShouldReplaceItsConnection()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        await CustomerAsync(fixture);
        var first = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));

        var second = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));

        Assert.Null(second.Error);
        var connections = await fixture.Context.AssistantConnections.OrderBy(c => c.RevokedAt == null).ToListAsync();
        Assert.Equal([false, true], connections.Select(c => c.IsActive));
        Assert.Null(await Authenticate(fixture, first.AccessToken));
        Assert.NotNull(await Authenticate(fixture, second.AccessToken));
    }

    [Fact]
    public async Task Renew_ShouldReplaceTheRefreshToken_AndAReplacedOneAgainShouldEndTheConnection_UnlessItLostARace()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var account = await CustomerAsync(fixture);
        var first = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(50);
        var renewed = await ExchangeAsync(fixture, Renewal(first.RefreshToken!, clientId));
        Assert.Null(renewed.Error);
        Assert.NotEqual(first.RefreshToken, renewed.RefreshToken);
        Assert.Equal(account, (await Authenticate(fixture, renewed.AccessToken))!.UserId);
        Assert.Equal(SalesTestFixture.Now.AddMinutes(50), (await fixture.Context.AssistantConnections.SingleAsync()).LastUsedAt);

        // Sent twice at once: the second gets nothing and the connection stays.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(50).AddSeconds(5);
        var twin = await ExchangeAsync(fixture, Renewal(first.RefreshToken!, clientId));
        Assert.Equal((OAuthErrors.InvalidGrant, ExchangeAssistantTokenCommandHandler.ConcurrentRenewalMessage), (twin.Error, twin.ErrorDescription));
        Assert.True((await fixture.Context.AssistantConnections.SingleAsync()).IsActive);
        // Another client with this token, or another resource, gets nothing either.
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, Renewal(renewed.RefreshToken!, new string('z', 43)))).Error);

        // Later: it was copied, and the connection ends with every token of it.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(51);
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, Renewal(first.RefreshToken!, clientId))).Error);
        Assert.False((await fixture.Context.AssistantConnections.SingleAsync()).IsActive);
        Assert.Null(await Authenticate(fixture, renewed.AccessToken));
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, Renewal(renewed.RefreshToken!, clientId))).Error);
    }

    [Fact]
    public async Task Authenticate_ShouldFindTheAccount_OnlyWithAnAccessTokenInItsHour_OfAConnectionAndAnAccountStillActive()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var account = await CustomerAsync(fixture);
        var tokens = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));

        var identity = await Authenticate(fixture, tokens.AccessToken);
        Assert.Equal((account, "cliente@demo.com"), (identity!.UserId, identity.Email));
        Assert.Equal(SalesTestFixture.Now, (await fixture.Context.AssistantConnections.SingleAsync()).LastUsedAt);
        // A refresh token, something that is not a token, or nothing, open nothing.
        Assert.Null(await Authenticate(fixture, tokens.RefreshToken));
        Assert.Null(await Authenticate(fixture, "Bearer abc"));
        Assert.Null(await Authenticate(fixture, null));

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddHours(1);
        Assert.Null(await Authenticate(fixture, tokens.AccessToken));

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(30);
        (await fixture.Context.Users.SingleAsync(u => u.Id == account)).IsActive = false;
        await fixture.Context.SaveChangesAsync();
        Assert.Null(await Authenticate(fixture, tokens.AccessToken));
    }

    [Fact]
    public async Task Revoke_ShouldEndTheConnectionOfItsToken_AndAnswerTheSameForAnyOther()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        await CustomerAsync(fixture);
        var tokens = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));
        var revoke = new RevokeAssistantTokenCommandHandler(fixture.Context, fixture.Clock);

        Assert.True((await revoke.Handle(new RevokeAssistantTokenCommand("nada", null), CancellationToken.None)).IsSuccess);
        Assert.True((await revoke.Handle(new RevokeAssistantTokenCommand(new string('q', 43), clientId), CancellationToken.None)).IsSuccess);
        // Another client's revocation stops nothing.
        Assert.True((await revoke.Handle(new RevokeAssistantTokenCommand(tokens.RefreshToken, new string('z', 43)), CancellationToken.None)).IsSuccess);
        Assert.NotNull(await Authenticate(fixture, tokens.AccessToken));

        Assert.True((await revoke.Handle(new RevokeAssistantTokenCommand(tokens.RefreshToken, clientId), CancellationToken.None)).IsSuccess);
        Assert.Null(await Authenticate(fixture, tokens.AccessToken));
        Assert.Equal(OAuthErrors.InvalidGrant, (await ExchangeAsync(fixture, Renewal(tokens.RefreshToken!, clientId))).Error);
    }

    [Fact]
    public async Task MiCuenta_ShouldListTheAccountsAssistants_AndDisconnectOnlyItsOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        var clientId = await RegisterAsync(fixture, Callback);
        var desktop = await RegisterAsync(fixture, "http://localhost:6274/callback");
        var other = await CustomerAsync(fixture, email: "otra@demo.com");
        await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, clientId), clientId));
        var account = await CustomerAsync(fixture);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(1);
        var mine = await ExchangeAsync(fixture, CodeExchange(await AllowAsync(fixture, desktop, "http://localhost:6274/callback"), desktop, "http://localhost:6274/callback"));

        var listed = await new GetMyAssistantConnectionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetMyAssistantConnectionsQuery(), CancellationToken.None);
        var connection = Assert.Single(listed);
        Assert.Equal(("Claude", "localhost:6274", SalesTestFixture.Now.AddMinutes(1)), (connection.Name, connection.Site, connection.GrantedAt));

        var disconnect = new DisconnectMyAssistantCommandHandler(fixture.Context, fixture.CurrentUser, fixture.Clock);
        var othersConnection = await fixture.Context.AssistantConnections.SingleAsync(c => c.UserId == other);
        Assert.Equal(DisconnectMyAssistantCommandHandler.NotFoundMessage,
            (await disconnect.Handle(new DisconnectMyAssistantCommand(othersConnection.Id), CancellationToken.None)).Error);
        Assert.True((await disconnect.Handle(new DisconnectMyAssistantCommand(connection.Id), CancellationToken.None)).IsSuccess);
        Assert.Null(await Authenticate(fixture, mine.AccessToken));
        Assert.Empty(await new GetMyAssistantConnectionsQueryHandler(fixture.Context, fixture.CurrentUser)
            .Handle(new GetMyAssistantConnectionsQuery(), CancellationToken.None));
        Assert.True((await fixture.Context.AssistantConnections.SingleAsync(c => c.Id == othersConnection.Id)).IsActive);
        Assert.NotEqual(account, other);
    }

    private static RegisterAssistantClientCommandHandler Register(SalesTestFixture fixture) => new(fixture.Context, fixture.Clock);

    private static async Task<string> RegisterAsync(SalesTestFixture fixture, params string[] redirectUris)
        => (await Register(fixture).Handle(new RegisterAssistantClientCommand { ClientName = "Claude", RedirectUris = [.. redirectUris] },
            CancellationToken.None)).ClientId!;

    /// <summary>A customer account (or another kind), signed in for what follows.</summary>
    private static async Task<Guid> CustomerAsync(
        SalesTestFixture fixture, UserRole role = UserRole.Customer, bool active = true, string email = "cliente@demo.com")
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{Guid.NewGuid():N}@demo.com",
            Email = email,
            FirstName = "María",
            LastName = "López",
            Role = role,
            IsActive = active
        };
        fixture.Context.Users.Add(user);
        await fixture.Context.SaveChangesAsync();
        fixture.CurrentUser.UserId = user.Id.ToString();
        fixture.CurrentUser.Role = role == UserRole.Customer ? "CUSTOMER" : "TENANT_ADMIN";
        return user.Id;
    }

    private static CheckAssistantAuthorizationQuery Request(string clientId) => new()
    {
        ResponseType = "code",
        ClientId = clientId,
        RedirectUri = Callback,
        CodeChallenge = Challenge,
        CodeChallengeMethod = "S256",
        Resource = Resource,
        Scope = "cuenta",
        State = "xyz 123"
    };

    private static ApproveAssistantAuthorizationCommand Approval(string clientId, bool allow, string redirectUri = Callback) => new()
    {
        ResponseType = "code",
        ClientId = clientId,
        RedirectUri = redirectUri,
        CodeChallenge = Challenge,
        CodeChallengeMethod = "S256",
        Resource = Resource,
        Scope = "cuenta",
        State = "xyz 123",
        Allow = allow
    };

    private static Task<AssistantAuthorizationCheckDto> Check(SalesTestFixture fixture, CheckAssistantAuthorizationQuery request)
        => new CheckAssistantAuthorizationQueryHandler(fixture.Context, new Server()).Handle(request, CancellationToken.None);

    private static ApproveAssistantAuthorizationCommandHandler Approve(SalesTestFixture fixture)
        => new(fixture.Context, fixture.CurrentUser, new Server(), fixture.Clock);

    /// <summary>The signed-in customer allows the assistant: the code its redirect carries.</summary>
    private static async Task<string> AllowAsync(SalesTestFixture fixture, string clientId, string redirectUri = Callback)
    {
        var allowed = await Approve(fixture).Handle(Approval(clientId, allow: true, redirectUri), CancellationToken.None);
        Assert.True(allowed.IsSuccess, allowed.Error);
        return CodeOf(allowed.Value!.RedirectUrl);
    }

    private static string CodeOf(string redirectUrl)
        => redirectUrl.Split('?', 2)[1].Split('&').Single(p => p.StartsWith("code=", StringComparison.Ordinal))["code=".Length..];

    private static ExchangeAssistantTokenCommand CodeExchange(string code, string clientId, string redirectUri = Callback) => new()
    {
        GrantType = "authorization_code",
        Code = code,
        RedirectUri = redirectUri,
        ClientId = clientId,
        CodeVerifier = Verifier,
        Resource = Resource
    };

    private static ExchangeAssistantTokenCommand Renewal(string refreshToken, string clientId) => new()
    {
        GrantType = "refresh_token",
        RefreshToken = refreshToken,
        ClientId = clientId,
        Resource = Resource
    };

    private static async Task<AssistantTokensDto> ExchangeAsync(SalesTestFixture fixture, ExchangeAssistantTokenCommand request)
    {
        var result = await new ExchangeAssistantTokenCommandHandler(
                fixture.Context, new NoLock(), new Server(), fixture.Clock, NullLogger<ExchangeAssistantTokenCommandHandler>.Instance)
            .Handle(request, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    private static Task<AssistantIdentityDto?> Authenticate(SalesTestFixture fixture, string? accessToken)
        => new AuthenticateAssistantCommandHandler(fixture.Context, fixture.Clock)
            .Handle(new AuthenticateAssistantCommand(accessToken), CancellationToken.None);

    private sealed class Server : IAssistantAccountServer
    {
        public string Resource => AssistantAuthorizationFlowTests.Resource;
    }

    private sealed class NoLock : IAssistantAuthorizationLock
    {
        public Task AcquireAsync(Guid key, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
