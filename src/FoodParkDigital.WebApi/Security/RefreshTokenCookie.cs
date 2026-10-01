namespace FoodParkDigital.WebApi.Security;

/// <summary>
/// Cookie HttpOnly del refresh token, compartida por todos los endpoints que inician sesión.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    public static void Append(HttpContext httpContext, IWebHostEnvironment environment, string refreshToken)
    {
        httpContext.Response.Cookies.Append(
            Name,
            refreshToken,
            CreateOptions(httpContext, environment, DateTime.UtcNow.AddDays(7)));
    }

    public static void Delete(HttpContext httpContext, IWebHostEnvironment environment)
    {
        httpContext.Response.Cookies.Delete(Name, CreateOptions(httpContext, environment));
    }

    private static CookieOptions CreateOptions(
        HttpContext httpContext,
        IWebHostEnvironment environment,
        DateTime? expires = null)
    {
        var isHttpsRequest = httpContext.Request.IsHttps;
        var secureCookie = !environment.IsDevelopment() && isHttpsRequest;

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secureCookie,
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.Strict,
            Expires = expires,
            Path = "/api/v1/auth"
        };
    }
}
