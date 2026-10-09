using FoodParkDigital.Application.Identity.DTOs;

namespace FoodParkDigital.WebApi.Security;

/// <summary>
/// Cookie HttpOnly del refresh token, compartida por todos los endpoints que inician sesión.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    /// <summary>
    /// La respuesta de un endpoint que inicia o renueva la sesión: el refresh token va solo en la cookie, nunca en el
    /// cuerpo que lee JavaScript.
    /// </summary>
    public static AuthResponse Issue(HttpContext httpContext, IWebHostEnvironment environment, AuthResponse response)
    {
        if (response.RefreshToken is not null)
            Append(httpContext, environment, response.RefreshToken);
        return response with { RefreshToken = null };
    }

    public static void Append(HttpContext httpContext, IWebHostEnvironment environment, string refreshToken)
    {
        httpContext.Response.Cookies.Append(
            Name,
            refreshToken,
            CreateOptions(environment, DateTime.UtcNow.AddDays(7)));
    }

    public static void Delete(HttpContext httpContext, IWebHostEnvironment environment)
    {
        httpContext.Response.Cookies.Delete(Name, CreateOptions(environment));
    }

    private static CookieOptions CreateOptions(IWebHostEnvironment environment, DateTime? expires = null)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            // Outside Development the cookie is always Secure, even when TLS terminates at a proxy and
            // the request reaches the app as HTTP (Request.IsHttps would then be false).
            Secure = !environment.IsDevelopment(),
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/v1/auth"
        };
    }
}
