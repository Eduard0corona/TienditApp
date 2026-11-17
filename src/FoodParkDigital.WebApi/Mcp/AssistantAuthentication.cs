using System.Security.Claims;
using System.Text.Encodings.Web;
using FoodParkDigital.Application.Assistants;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Assistants.Entities;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.WebApi.Mcp;

/// <summary>
/// How /mcp/cuenta knows the customer (Fase 32E, ADR 0097): a scheme of its own that reads the access token an assistant got
/// with the customer's permission, never the app's session. Without a valid one, the answer is 401 with where to learn how to
/// get one (RFC 9728), as the MCP specification asks.
/// </summary>
public static class AssistantAuthentication
{
    public const string Scheme = "AssistantToken";
    public const string Policy = "AssistantAccount";
    public const string ConnectionClaim = "assistant_connection";

    /// <summary>The role of the identity it builds: the one of a customer account.</summary>
    public const string CustomerRole = "CUSTOMER";

    /// <summary>Whether <paramref name="user"/> came from an assistant's access token: only then the account's tools answer.</summary>
    public static bool IsAccount(ClaimsPrincipal? user) => user?.Identity is { IsAuthenticated: true, AuthenticationType: Scheme };

    /// <summary>Where an assistant reads how to get a token for /mcp/cuenta.</summary>
    public static string ResourceMetadataUrl(HttpContext context) => AssistantOAuth.OriginOf(context) + AssistantOAuth.ProtectedResourcePath;

    public static IServiceCollection AddAssistantAuthentication(this IServiceCollection services)
    {
        services.AddScoped<IAssistantAccountServer, AssistantAccountServer>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AssistantTokenHandler>(Scheme, displayName: null, _ => { });
        services.AddAuthorization(options => options.AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()));
        return services;
    }
}

/// <summary>
/// The access token of an assistant, from its Authorization header: the identity of the customer account it opens (its id,
/// its email and the role of a customer), or nothing. The token never reaches a log.
/// </summary>
public sealed class AssistantTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private const string Bearer = "Bearer ";

    private readonly ISender _mediator;

    public AssistantTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISender mediator)
        : base(options, logger, encoder)
    {
        _mediator = mediator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var identity = await _mediator.Send(new AuthenticateAssistantCommand(header[Bearer.Length..].Trim()), Context.RequestAborted);
        if (identity is null)
            return AuthenticateResult.Fail("The access token is invalid, expired or of a disconnected assistant.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Role, AssistantAuthentication.CustomerRole),
            new(AssistantAuthentication.ConnectionClaim, identity.ConnectionId.ToString())
        };
        if (!string.IsNullOrEmpty(identity.Email))
            claims.Add(new Claim(ClaimTypes.Email, identity.Email));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AssistantAuthentication.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, AssistantAuthentication.Scheme));
    }

    /// <summary>401 with the address of the resource's metadata and its scope (RFC 6750 and 9728); a token that was sent and failed says so.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        var challenge = $"Bearer resource_metadata=\"{AssistantAuthentication.ResourceMetadataUrl(Context)}\", scope=\"{AssistantConnection.AccountScope}\"";
        if (result.Failure is not null)
            challenge += ", error=\"invalid_token\", error_description=\"The access token is invalid or expired\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = challenge;
    }
}

/// <summary>The account's MCP server at the address of this request (<see cref="AssistantOAuth.OriginOf"/>): what every code and token is for.</summary>
public sealed class AssistantAccountServer : IAssistantAccountServer
{
    private readonly IHttpContextAccessor _http;

    public AssistantAccountServer(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string Resource => _http.HttpContext is { } context
        ? AssistantOAuth.OriginOf(context) + McpServer.AccountPath
        : throw new InvalidOperationException("The account's MCP server is only known within a request.");
}
