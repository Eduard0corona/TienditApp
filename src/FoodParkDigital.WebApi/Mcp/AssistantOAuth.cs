using System.Text.Json;
using FoodParkDigital.Application.Assistants;
using FoodParkDigital.Domain.Assistants.Entities;
using FoodParkDigital.WebApi.Http;
using MediatR;
using Microsoft.AspNetCore.Http.Features;

namespace FoodParkDigital.WebApi.Mcp;

/// <summary>
/// The authorization server of /mcp/cuenta (Fase 32E, ADR 0097), in the app itself, for AI assistants: the metadata of the
/// protected resource (RFC 9728) and of the server (RFC 8414), the dynamic registration of public clients (RFC 7591), the
/// authorization endpoint that sends the browser to the permission page, the token endpoint (codes with PKCE and refresh
/// tokens that rotate) and revocation (RFC 7009). Out of the API's contract: its clients are assistants, which speak OAuth.
/// Tokens never reach a log, and its answers are never cached.
/// </summary>
public static class AssistantOAuth
{
    public const string ProtectedResourcePath = "/.well-known/oauth-protected-resource" + McpServer.AccountPath;
    public const string AuthorizationServerPath = "/.well-known/oauth-authorization-server";
    public const string AuthorizePath = "/oauth/authorize";
    public const string TokenPath = "/oauth/token";
    public const string RegisterPath = "/oauth/register";
    public const string RevokePath = "/oauth/revoke";

    /// <summary>The permission page of the app, where /oauth/authorize sends the browser.</summary>
    public const string ConsentPath = "/autorizar";

    /// <summary>Registering a client, per address: an assistant registers one when a customer adds it, not dozens a minute.</summary>
    public const string RegisterRateLimitPolicy = "assistant-register";

    /// <summary>The largest request it reads: any registration, code or token fits in it several times.</summary>
    public const long MaxRequestBytes = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] Scopes = [AssistantConnection.AccountScope];
    private static readonly string[] GrantTypes = [AssistantAuthorization.AuthorizationCodeGrant, AssistantAuthorization.RefreshTokenGrant];
    private static readonly string[] ResponseTypes = [AssistantAuthorization.ResponseType];
    private static readonly string[] AuthMethods = [AssistantAuthorization.ClientAuthenticationMethod];

    public static void MapAssistantAuthorization(this WebApplication app)
    {
        app.MapGet(ProtectedResourcePath, ProtectedResource).RequireRateLimiting(McpServer.RateLimitPolicy).ExcludeFromDescription();
        app.MapGet(AuthorizationServerPath, AuthorizationServer).RequireRateLimiting(McpServer.RateLimitPolicy).ExcludeFromDescription();
        app.MapPost(RegisterPath, RegisterAsync).RequireRateLimiting(RegisterRateLimitPolicy).ExcludeFromDescription();
        app.MapGet(AuthorizePath, AuthorizeAsync).RequireRateLimiting(McpServer.RateLimitPolicy).ExcludeFromDescription();
        app.MapPost(TokenPath, TokenAsync).RequireRateLimiting(McpServer.RateLimitPolicy).ExcludeFromDescription();
        app.MapPost(RevokePath, RevokeAsync).RequireRateLimiting(McpServer.RateLimitPolicy).ExcludeFromDescription();
    }

    /// <summary>RFC 9728: the account's MCP server, who issues its tokens and its only scope.</summary>
    private static IResult ProtectedResource(HttpContext context)
    {
        var baseUrl = PublicPages.BaseUrl(context);
        return Answer(context, new
        {
            Resource = baseUrl + McpServer.AccountPath,
            AuthorizationServers = new[] { baseUrl },
            ScopesSupported = Scopes,
            BearerMethodsSupported = new[] { "header" },
            ResourceName = "TienditApp con tu cuenta",
            ResourceDocumentation = baseUrl + "/asistentes"
        });
    }

    /// <summary>RFC 8414: its endpoints and what it supports — the code flow with PKCE S256, for public clients.</summary>
    private static IResult AuthorizationServer(HttpContext context)
    {
        var baseUrl = PublicPages.BaseUrl(context);
        return Answer(context, new
        {
            Issuer = baseUrl,
            AuthorizationEndpoint = baseUrl + AuthorizePath,
            TokenEndpoint = baseUrl + TokenPath,
            RegistrationEndpoint = baseUrl + RegisterPath,
            RevocationEndpoint = baseUrl + RevokePath,
            ScopesSupported = Scopes,
            ResponseTypesSupported = ResponseTypes,
            GrantTypesSupported = GrantTypes,
            CodeChallengeMethodsSupported = new[] { AssistantAuthorization.ChallengeMethod },
            TokenEndpointAuthMethodsSupported = AuthMethods,
            RevocationEndpointAuthMethodsSupported = AuthMethods,
            ServiceDocumentation = baseUrl + "/asistentes"
        });
    }

    /// <summary>RFC 7591: a public client with its redirect_uris; 201 with its client_id, or 400 with why not.</summary>
    private static async Task<IResult> RegisterAsync(HttpContext context, ISender mediator)
    {
        if (TooLarge(context))
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        Registration? registration;
        try
        {
            registration = await context.Request.ReadFromJsonAsync<Registration>(Json, context.RequestAborted);
        }
        catch (Exception reading) when (reading is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            registration = null;
        }
        if (registration is null)
            return Error(context, OAuthErrors.InvalidClientMetadata, "Envía los metadatos del cliente en JSON.");

        var registered = await mediator.Send(new RegisterAssistantClientCommand
        {
            ClientName = registration.ClientName,
            RedirectUris = registration.RedirectUris,
            TokenEndpointAuthMethod = registration.TokenEndpointAuthMethod,
            GrantTypes = registration.GrantTypes,
            ResponseTypes = registration.ResponseTypes
        }, context.RequestAborted);
        if (registered.Error is not null)
            return Error(context, registered.Error, registered.ErrorDescription);

        return Answer(context, new
        {
            registered.ClientId,
            ClientIdIssuedAt = registered.IssuedAt.ToUnixTimeSeconds(),
            registered.ClientName,
            registered.RedirectUris,
            GrantTypes,
            ResponseTypes,
            TokenEndpointAuthMethod = AssistantAuthorization.ClientAuthenticationMethod
        }, StatusCodes.Status201Created);
    }

    /// <summary>
    /// The start of the permission: the browser goes to the permission page with the same parameters, or back to the assistant
    /// with the error of a request it can fix. A client or address that is not right is said on the page, never followed.
    /// </summary>
    private static async Task<IResult> AuthorizeAsync(HttpContext context, ISender mediator)
    {
        var query = context.Request.Query;
        var check = await mediator.Send(new CheckAssistantAuthorizationQuery
        {
            ResponseType = query["response_type"],
            ClientId = query["client_id"],
            RedirectUri = query["redirect_uri"],
            CodeChallenge = query["code_challenge"],
            CodeChallengeMethod = query["code_challenge_method"],
            Resource = query["resource"],
            Scope = query["scope"],
            State = query["state"]
        }, context.RequestAborted);
        NoStore(context);
        return check.ErrorRedirectUrl is { } back
            ? Results.Redirect(back)
            : Results.Redirect(ConsentPath + context.Request.QueryString);
    }

    /// <summary>RFC 6749, 4.1.3 and 6: tokens for a code or a refresh token, form-encoded; 400 with the OAuth error otherwise.</summary>
    private static async Task<IResult> TokenAsync(HttpContext context, ISender mediator)
    {
        if (TooLarge(context))
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!context.Request.HasFormContentType)
            return Error(context, OAuthErrors.InvalidRequest, "Envía la solicitud como application/x-www-form-urlencoded.");

        if (await FormOf(context) is not { } form)
            return Error(context, OAuthErrors.InvalidRequest, "La solicitud no se pudo leer.");
        var result = await mediator.Send(new ExchangeAssistantTokenCommand
        {
            GrantType = form["grant_type"],
            Code = form["code"],
            RedirectUri = form["redirect_uri"],
            ClientId = form["client_id"],
            CodeVerifier = form["code_verifier"],
            RefreshToken = form["refresh_token"],
            Resource = form["resource"]
        }, context.RequestAborted);
        var tokens = result.Value!;
        if (tokens.Error is not null)
            return Error(context, tokens.Error, tokens.ErrorDescription);

        return Answer(context, new
        {
            tokens.AccessToken,
            TokenType = "Bearer",
            tokens.ExpiresIn,
            tokens.RefreshToken,
            tokens.Scope
        });
    }

    /// <summary>RFC 7009: 200 whether the token was one or not.</summary>
    private static async Task<IResult> RevokeAsync(HttpContext context, ISender mediator)
    {
        if (TooLarge(context))
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!context.Request.HasFormContentType)
            return Error(context, OAuthErrors.InvalidRequest, "Envía la solicitud como application/x-www-form-urlencoded.");

        if (await FormOf(context) is not { } form)
            return Error(context, OAuthErrors.InvalidRequest, "La solicitud no se pudo leer.");
        await mediator.Send(new RevokeAssistantTokenCommand(form["token"], form["client_id"]), context.RequestAborted);
        NoStore(context);
        return Results.Ok();
    }

    /// <summary>The form of the request; null when it cannot be read (malformed, or larger than allowed).</summary>
    private static async Task<IFormCollection?> FormOf(HttpContext context)
    {
        try
        {
            return await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (Exception reading) when (reading is InvalidDataException or BadHttpRequestException)
        {
            return null;
        }
    }

    private static IResult Answer(HttpContext context, object body, int status = StatusCodes.Status200OK)
    {
        NoStore(context);
        return Results.Json(body, Json, statusCode: status);
    }

    private static IResult Error(HttpContext context, string error, string? description)
        => Answer(context, new { Error = error, ErrorDescription = description }, StatusCodes.Status400BadRequest);

    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    /// <summary>A body larger than any of its requests is refused before it is read.</summary>
    private static bool TooLarge(HttpContext context)
    {
        if (context.Request.ContentLength > MaxRequestBytes)
            return true;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
            body.MaxRequestBodySize = MaxRequestBytes;
        return false;
    }

    private sealed record Registration(
        string? ClientName,
        List<string>? RedirectUris,
        string? TokenEndpointAuthMethod,
        List<string>? GrantTypes,
        List<string>? ResponseTypes);
}
