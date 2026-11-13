using System.Reflection;
using System.Security.Claims;
using FoodParkDigital.WebApi.Controllers;
using FoodParkDigital.WebApi.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Only credential and code endpoints carry the "auth" rate limit. Refresh, profile and tenant endpoints
/// run on every screen of every device of a business: limiting them made the client end sessions.
/// </summary>
public class AuthRateLimitingTests
{
    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.Login))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.RegisterBusiness))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.RegisterCustomer))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.ConfirmEmail))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.ResendConfirmationEmail))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.ConfirmPhone))]
    [InlineData(typeof(RegistrationController), nameof(RegistrationController.ResendPhoneConfirmation))]
    [InlineData(typeof(PasswordRecoveryController), nameof(PasswordRecoveryController.ForgotPassword))]
    [InlineData(typeof(PasswordRecoveryController), nameof(PasswordRecoveryController.ResetPassword))]
    [InlineData(typeof(ProfileController), nameof(ProfileController.ChangePassword))]
    [InlineData(typeof(TwoFactorController), nameof(TwoFactorController.Login2FA))]
    [InlineData(typeof(TwoFactorController), nameof(TwoFactorController.Verify2FA))]
    [InlineData(typeof(TwoFactorController), nameof(TwoFactorController.Disable2FA))]
    public void CredentialAndCodeEndpoints_ShouldBeRateLimited(Type controller, string action)
    {
        Assert.Equal("auth", PolicyOf(controller, action));
    }

    [Theory]
    [InlineData(typeof(AuthController), nameof(AuthController.RefreshToken))]
    [InlineData(typeof(AuthController), nameof(AuthController.Logout))]
    [InlineData(typeof(ProfileController), nameof(ProfileController.GetProfile))]
    [InlineData(typeof(AuthController), nameof(AuthController.SwitchTenant))]
    public void SessionEndpoints_ShouldNotBeRateLimited(Type controller, string action)
    {
        Assert.Null(PolicyOf(controller, action));
    }

    [Theory]
    [InlineData(nameof(MeController.SaveMyReview))]
    [InlineData(nameof(MeController.DeleteMyReview))]
    public void ReviewsOfTheAccount_ShouldBeRateLimited_AsANewOneEmailsTheBusiness(string action)
    {
        // Deleting and writing again in a loop would flood the team's inbox (Fase 31K, ADR 0091): per address, and per
        // account once its session is read.
        Assert.Equal(MeController.ReviewsRateLimitPolicy, PolicyOf(typeof(MeController), action));
        var perAccount = typeof(MeController).GetMethod(action)!.GetCustomAttribute<ServiceFilterAttribute>();
        Assert.Equal(typeof(MyReviewsAccountLimit), perAccount?.ServiceType);
        Assert.Null(PolicyOf(typeof(MeController), nameof(MeController.GetMyReviews)));
    }

    [Fact]
    public void TheAccountLimit_ShouldBeOneForTheWholeApp_SoEveryRequestOfAnAccountCountsInIt()
    {
        // One per request would start each at 0, and an account would never reach its limit.
        using var api = new HealthEndpointsTests.ApiWithoutDatabase();
        using var first = api.Services.CreateScope();
        using var second = api.Services.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<MyReviewsAccountLimit>(),
            second.ServiceProvider.GetRequiredService<MyReviewsAccountLimit>());
    }

    [Fact]
    public async Task ReviewsOfTheAccount_ShouldBeCountedPerAccount_NotPerTheAddressTheyShare()
    {
        using var limit = new MyReviewsAccountLimit();

        // Two customers behind the same address (a food park's Wi-Fi): one spends its hour, the other still writes.
        for (var i = 0; i < MyReviewsAccountLimit.PerHour; i++)
            Assert.True(await PassesAsync(limit, "cuenta-a"));
        Assert.False(await PassesAsync(limit, "cuenta-a"));
        Assert.True(await PassesAsync(limit, "cuenta-b"));
    }

    /// <summary>One request of <paramref name="account"/> through the filter: whether it reached the action.</summary>
    private static async Task<bool> PassesAsync(MyReviewsAccountLimit limit, string account)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account)], "test"))
        };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], new Dictionary<string, object?>(), new object());
        var reached = false;
        await limit.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });
        if (!reached)
        {
            var refused = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal(StatusCodes.Status429TooManyRequests, refused.StatusCode);
            Assert.Equal(MyReviewsAccountLimit.Message, Assert.IsType<ProblemDetails>(refused.Value).Detail);
        }
        return reached;
    }

    private static string? PolicyOf(Type controller, string action)
    {
        var method = controller.GetMethod(action)
            ?? throw new InvalidOperationException($"{controller.Name}.{action} does not exist.");
        return method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName
            ?? controller.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;
    }
}
