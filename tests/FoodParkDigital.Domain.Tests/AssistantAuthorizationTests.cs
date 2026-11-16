using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.Domain.Assistants.Enums;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The authorization an AI assistant gets for a customer account (Fase 32E, ADR 0097): a registered client with the exact
/// addresses its codes return to, a code of one use tied to PKCE, the connection the customer sees in Mi cuenta and its
/// tokens, stored only as their hash.
/// </summary>
public class AssistantAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.NewGuid();
    private const string ClientId = "c1ient-id-c1ient-id-c1ient-id-c1ient-id-c1x";
    private const string Code = "code-code-code-code-code-code-code-code-abc";
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Resource = "https://app.test/mcp/cuenta";
    // RFC 7636, appendix B.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private static AssistantClient Client(params string[] redirectUris)
        => AssistantClient.Register(ClientId, "Claude", redirectUris.Length == 0 ? [Callback] : redirectUris, Now);

    private static AssistantAuthorizationCode Issued(AssistantClient? client = null, string redirectUri = Callback)
        => AssistantAuthorizationCode.Issue(client ?? Client(), Account, Code, redirectUri, Challenge, Resource, "cuenta", Now);

    [Fact]
    public void Secrets_ShouldHaveTheShapeOfTrackingLinks_AndBeStoredAsTheirHash()
    {
        Assert.Equal(43, Code.Length);
        Assert.True(AssistantSecrets.IsWellFormed(Code));
        Assert.False(AssistantSecrets.IsWellFormed(Code + "x"));
        Assert.False(AssistantSecrets.IsWellFormed(Code[..42] + "="));
        Assert.False(AssistantSecrets.IsWellFormed(null));

        var hash = AssistantSecrets.HashOf(Code);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(Code, hash);
        Assert.Equal(hash, AssistantSecrets.HashOf(Code));
    }

    [Fact]
    public void Pkce_ShouldCheck_TheVerifierOfS256_AsRfc7636Shows()
    {
        Assert.Equal(Challenge, AssistantSecrets.ChallengeOf(Verifier));
        Assert.True(AssistantSecrets.Verifies(Challenge, Verifier));
        Assert.False(AssistantSecrets.Verifies(Challenge, Verifier[..^1] + "l"));
        // Too short or outside its alphabet is not a verifier, whatever it hashes to.
        Assert.False(AssistantSecrets.IsCodeVerifier(Verifier[..42]));
        Assert.False(AssistantSecrets.IsCodeVerifier(new string('a', 129)));
        Assert.False(AssistantSecrets.IsCodeVerifier(Verifier[..42] + "+"));
        Assert.True(AssistantSecrets.IsCodeVerifier(new string('~', 128)));
        Assert.False(AssistantSecrets.Verifies(Challenge, null));
    }

    [Fact]
    public void Register_ShouldKeep_ItsAddresses_AndAShortCleanName()
    {
        var client = AssistantClient.Register(ClientId, "  Claude\n\tDesktop\u0007 ", [Callback, Callback, "http://localhost:33418/callback"], Now);

        Assert.Equal(ClientId, client.ClientId);
        Assert.Equal("Claude Desktop", client.Name);
        Assert.Equal([Callback, "http://localhost:33418/callback"], client.RedirectUris);
        Assert.Equal(Now, client.RegisteredAt);
        Assert.Null(AssistantClient.Register(ClientId, " \n ", [Callback], Now).Name);
        Assert.Equal(100, AssistantClient.Register(ClientId, new string('n', 150), [Callback], Now).Name!.Length);
    }

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://chatgpt.com/connector_platform_oauth_redirect?x=1", true)]
    [InlineData("http://localhost:6274/oauth/callback", true)]
    [InlineData("http://127.0.0.1/callback", true)]
    [InlineData("http://[::1]:8080/callback", true)]
    [InlineData("http://claude.ai/callback", false)]
    [InlineData("https://claude.ai/callback#fragment", false)]
    [InlineData("https://claude.ai@evil.example/callback", false)]
    [InlineData("https://claude.ai/call back", false)]
    [InlineData("cursor://anysphere.cursor-retrieval/oauth/callback", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/callback", false)]
    [InlineData("", false)]
    public void IsRedirectUri_ShouldTake_HttpsOrThisDevice_WithoutAFragmentOrAUser(string uri, bool taken)
        => Assert.Equal(taken, AssistantClient.IsRedirectUri(uri));

    [Fact]
    public void Register_ShouldRefuse_NoAddress_TooMany_OrOneThatIsNot()
    {
        Assert.Equal(AssistantClient.RedirectUrisRequiredMessage,
            Assert.Throws<DomainException>(() => AssistantClient.Register(ClientId, "x", [], Now)).Message);
        Assert.Equal(AssistantClient.RedirectUrisRequiredMessage,
            Assert.Throws<DomainException>(() => AssistantClient.Register(ClientId, "x", null, Now)).Message);
        Assert.Equal(AssistantClient.TooManyRedirectUrisMessage, Assert.Throws<DomainException>(() =>
            AssistantClient.Register(ClientId, "x", Enumerable.Range(0, 11).Select(i => $"https://a.test/{i}"), Now)).Message);
        Assert.Equal(AssistantClient.InvalidRedirectUriMessage,
            Assert.Throws<DomainException>(() => AssistantClient.Register(ClientId, "x", [Callback, "http://evil.example/cb"], Now)).Message);
        Assert.Equal(AssistantClient.ClientIdRequiredMessage,
            Assert.Throws<DomainException>(() => AssistantClient.Register("short", "x", [Callback], Now)).Message);
    }

    [Fact]
    public void AllowsRedirect_ShouldBeExact_ButTakeAnyPortOnThisDevice()
    {
        var client = Client(Callback, "http://127.0.0.1:33418/callback");

        Assert.True(client.AllowsRedirect(Callback));
        Assert.False(client.AllowsRedirect(Callback + "/"));
        Assert.False(client.AllowsRedirect(Callback + "?next=x"));
        Assert.False(client.AllowsRedirect("https://CLAUDE.ai/api/mcp/auth_callback"));
        // A desktop app listens on whichever port is free (RFC 8252), on the same host and path.
        Assert.True(client.AllowsRedirect("http://127.0.0.1:50123/callback"));
        Assert.True(client.AllowsRedirect("http://127.0.0.1/callback"));
        Assert.False(client.AllowsRedirect("http://localhost:50123/callback"));
        Assert.False(client.AllowsRedirect("http://127.0.0.1:50123/other"));
        Assert.False(client.AllowsRedirect("https://127.0.0.1:33418/callback"));
        Assert.False(client.AllowsRedirect(null));
    }

    [Fact]
    public void Issue_ShouldTie_TheCode_ToEverythingTheRequestSaid_ForFiveMinutes()
    {
        var client = Client();
        var code = Issued(client);

        Assert.Equal(client.Id, code.ClientId);
        Assert.Equal(Account, code.UserId);
        Assert.Equal(AssistantSecrets.HashOf(Code), code.CodeHash);
        Assert.Equal(Callback, code.RedirectUri);
        Assert.Equal(Challenge, code.CodeChallenge);
        Assert.Equal(Resource, code.Resource);
        Assert.Equal("cuenta", code.Scope);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), code.ExpiresAt);
        Assert.False(code.IsExpired(Now.AddMinutes(4)));
        Assert.True(code.IsExpired(Now.AddMinutes(5)));
        Assert.True(code.Verifies(Verifier));
        Assert.False(code.Verifies("x"));
    }

    [Fact]
    public void Issue_ShouldRefuse_AnAddressNotOfTheClient_AChallengeThatIsNot_OrAnotherScope()
    {
        var client = Client();
        Assert.Throws<DomainException>(() => Issued(client, "https://evil.example/cb"));
        Assert.Throws<DomainException>(() => AssistantAuthorizationCode.Issue(client, Account, Code, Callback, "plain", Resource, "cuenta", Now));
        Assert.Throws<DomainException>(() => AssistantAuthorizationCode.Issue(client, Account, Code, Callback, Challenge, Resource, "admin", Now));
        Assert.Throws<DomainException>(() => AssistantAuthorizationCode.Issue(client, Guid.Empty, Code, Callback, Challenge, Resource, "cuenta", Now));
        Assert.Throws<DomainException>(() => AssistantAuthorizationCode.Issue(client, Account, "short", Callback, Challenge, Resource, "cuenta", Now));
        Assert.Throws<DomainException>(() => AssistantAuthorizationCode.Issue(client, Account, Code, Callback, Challenge, " ", "cuenta", Now));
    }

    [Fact]
    public void Redeem_ShouldHappen_OnceAndInItsTime()
    {
        var connection = Guid.NewGuid();
        var code = Issued();

        code.Redeem(connection, Now.AddMinutes(1));

        Assert.Equal(Now.AddMinutes(1), code.RedeemedAt);
        Assert.Equal(connection, code.ConnectionId);
        Assert.Equal(AssistantAuthorizationCode.AlreadyRedeemedMessage,
            Assert.Throws<DomainException>(() => code.Redeem(Guid.NewGuid(), Now.AddMinutes(2))).Message);
        Assert.Equal(AssistantAuthorizationCode.ExpiredMessage,
            Assert.Throws<DomainException>(() => Issued().Redeem(connection, Now.AddMinutes(5))).Message);
    }

    [Fact]
    public void Connection_ShouldBeForTheAccount_StopOnce_AndWriteItsUseEveryFiveMinutes()
    {
        var client = Guid.NewGuid();
        var connection = AssistantConnection.Grant(Account, client, "cuenta", Now);

        Assert.Equal(Account, connection.UserId);
        Assert.Equal(client, connection.ClientId);
        Assert.Equal(Now, connection.GrantedAt);
        Assert.True(connection.IsActive);
        Assert.Null(connection.LastUsedAt);

        Assert.True(connection.RecordUse(Now.AddMinutes(1)));
        Assert.False(connection.RecordUse(Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(1), connection.LastUsedAt);
        Assert.True(connection.RecordUse(Now.AddMinutes(6)));

        connection.Revoke(Now.AddMinutes(7));
        connection.Revoke(Now.AddMinutes(8));
        Assert.False(connection.IsActive);
        Assert.Equal(Now.AddMinutes(7), connection.RevokedAt);
        Assert.False(connection.RecordUse(Now.AddHours(1)));

        Assert.Throws<DomainException>(() => AssistantConnection.Grant(Account, client, "todo", Now));
        Assert.Throws<DomainException>(() => AssistantConnection.Grant(Guid.Empty, client, "cuenta", Now));
        Assert.Throws<DomainException>(() => AssistantConnection.Grant(Account, Guid.Empty, "cuenta", Now));
    }

    [Fact]
    public void Tokens_ShouldLast_AnHourToOpen_And30DaysToRenew_OnlyTheRenewalOneRotating_Once()
    {
        var connection = Guid.NewGuid();
        var access = AssistantToken.Issue(connection, AssistantTokenKind.Access, Code, Now);
        var refresh = AssistantToken.Issue(connection, AssistantTokenKind.Refresh, Code, Now);

        Assert.Equal(AssistantSecrets.HashOf(Code), access.TokenHash);
        Assert.Equal(Now.AddHours(1), access.ExpiresAt);
        Assert.Equal(Now.AddDays(30), refresh.ExpiresAt);
        Assert.True(access.IsUsable(Now.AddMinutes(59)));
        Assert.False(access.IsUsable(Now.AddHours(1)));

        refresh.Rotate(Now.AddDays(1));
        Assert.False(refresh.IsUsable(Now.AddDays(1)));
        Assert.Equal(AssistantToken.AlreadyRotatedMessage, Assert.Throws<DomainException>(() => refresh.Rotate(Now.AddDays(2))).Message);
        Assert.Throws<DomainException>(() => access.Rotate(Now));
        Assert.Throws<DomainException>(() => AssistantToken.Issue(Guid.Empty, AssistantTokenKind.Access, Code, Now));
        Assert.Throws<DomainException>(() => AssistantToken.Issue(connection, (AssistantTokenKind)9, Code, Now));
        Assert.Throws<DomainException>(() => AssistantToken.Issue(connection, AssistantTokenKind.Access, "short", Now));
    }
}
