using System.Reflection;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The account endpoints under /api/v1/auth: the session, registration, password recovery, the profile and the business
/// of the session. They were the 33 actions of one AuthController until the Fase 14 split it, and each one keeps its
/// method, route, who may call it and its rate limit, under the "Auth" group of the contract.
/// </summary>
public class AccountEndpointsTests
{
    private static readonly Type[] AccountControllers =
    [
        typeof(AuthController),
        typeof(RegistrationController),
        typeof(PasswordRecoveryController),
        typeof(ProfileController),
        typeof(MyTenantController)
    ];

    // Method, route, who may call it (anyone or a signed-in account) and the rate limit policy.
    private static readonly string[] Endpoints =
    [
        "POST api/v1/auth/login anyone auth",
        "POST api/v1/auth/refresh-token anyone -",
        "POST api/v1/auth/logout anyone -",
        "POST api/v1/auth/switch-tenant signed-in -",
        "POST api/v1/auth/register/business anyone auth",
        "POST api/v1/auth/register/customer anyone auth",
        "GET api/v1/auth/confirm-email anyone auth",
        "POST api/v1/auth/resend-confirmation-email anyone auth",
        "POST api/v1/auth/confirm-phone anyone auth",
        "POST api/v1/auth/resend-phone-confirmation anyone auth",
        "POST api/v1/auth/change-unconfirmed-phone anyone auth",
        "GET api/v1/auth/invitation anyone auth",
        "POST api/v1/auth/accept-invitation anyone auth",
        "POST api/v1/auth/forgot-password anyone auth",
        "POST api/v1/auth/reset-password anyone auth",
        "POST api/v1/auth/forgot-password/sms anyone auth",
        "POST api/v1/auth/reset-password/sms anyone auth",
        "GET api/v1/auth/profile signed-in -",
        "PUT api/v1/auth/profile signed-in -",
        "POST api/v1/auth/profile/avatar signed-in -",
        "DELETE api/v1/auth/profile/avatar signed-in -",
        "POST api/v1/auth/profile/phone signed-in auth",
        "POST api/v1/auth/profile/phone/confirm signed-in auth",
        "POST api/v1/auth/profile/change-password signed-in auth",
        "GET api/v1/auth/profile/whatsapp signed-in -",
        "PUT api/v1/auth/profile/whatsapp signed-in -",
        "GET api/v1/auth/my-tenant signed-in -",
        "PUT api/v1/auth/my-tenant signed-in -",
        "POST api/v1/auth/my-tenant/logo signed-in -",
        "DELETE api/v1/auth/my-tenant/logo signed-in -",
        "POST api/v1/auth/my-tenant/cover signed-in -",
        "DELETE api/v1/auth/my-tenant/cover signed-in -",
        "GET api/v1/auth/my-tenant/whatsapp signed-in -",
        "PUT api/v1/auth/my-tenant/whatsapp signed-in -",
        "POST api/v1/auth/my-tenant/whatsapp/send signed-in -"
    ];

    [Fact]
    public void AccountEndpoints_ShouldKeepTheirRoute_WhoMayCallThem_AndTheirRateLimit()
    {
        var actual = AccountControllers.SelectMany(EndpointsOf).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(Endpoints.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void AccountControllers_ShouldStayInTheAuthGroupOfTheContract()
    {
        Assert.All(AccountControllers, controller =>
        {
            var tags = controller.GetCustomAttribute<TagsAttribute>()?.Tags ?? [controller.Name[..^"Controller".Length]];
            Assert.Equal("Auth", Assert.Single(tags));
        });
    }

    private static IEnumerable<string> EndpointsOf(Type controller)
    {
        var route = controller.GetCustomAttribute<RouteAttribute>()!.Template
            .Replace("[controller]", controller.Name[..^"Controller".Length].ToLowerInvariant(), StringComparison.Ordinal);
        return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>().SelectMany(action => action.HttpMethods.Select(verb =>
            {
                var path = string.IsNullOrEmpty(action.Template) ? route : $"{route}/{action.Template}";
                var anonymous = method.IsDefined(typeof(AllowAnonymousAttribute)) || controller.IsDefined(typeof(AllowAnonymousAttribute));
                var signedIn = method.IsDefined(typeof(AuthorizeAttribute)) || controller.IsDefined(typeof(AuthorizeAttribute));
                var access = anonymous ? "anyone" : signedIn ? "signed-in" : "undeclared";
                var limit = method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
                    ?? controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
                    ?? "-";
                return $"{verb} {path} {access} {limit}";
            })));
    }
}
