using System.Reflection;
using System.Text.RegularExpressions;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Every controller with data of a business is called by the suite between two businesses (the CrossTenant* classes and
/// <see cref="TwoBusinessesApi"/>): a new one fails here until a test of the suite calls its routes with what belongs to
/// the other business. Each URL of the suite counts for the controller of the longest action route that covers it:
/// several controllers share /api/v1/auth and /api/v1/auth/my-tenant, so /api/v1/auth/my-tenant counts for the one
/// that answers it and not for the guided setup under it. The ones outside on purpose say why.
/// </summary>
public class CrossTenantSuiteCoverageTests
{
    private static readonly Dictionary<string, string> OutsideTheSuite = new(StringComparer.Ordinal)
    {
        [nameof(PasswordRecoveryController)] = "Recuperar la contraseña es de la cuenta y anónimo: no tiene datos de un negocio.",
        [nameof(ProfileController)] = "El perfil es de la cuenta (datos, avatar, teléfono, contraseña y el WhatsApp de sus códigos): no tiene datos de un negocio.",
        [nameof(TwoFactorController)] = "El 2FA es de la cuenta: no tiene datos de un negocio."
    };

    [Fact]
    public void EveryControllerWithDataOfABusiness_ShouldBeCalledByTheSuite()
    {
        var called = CalledControllers(SuiteUrls(), Controllers());

        var missing = Controllers()
            .Where(controller => !OutsideTheSuite.ContainsKey(controller.Name) && !called.Contains(controller.Name))
            .Select(controller => $"{controller.Name} (/{RouteOf(controller)})")
            .ToList();

        Assert.True(missing.Count == 0,
            "Controller sin prueba en la suite entre negocios. Agrega a una clase CrossTenant* una prueba que llame sus " +
            "rutas con lo de otro negocio o, si no tiene datos de un negocio, agrégalo a OutsideTheSuite con el motivo: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void OutsideTheSuite_ShouldOnlyNameControllersThatExist_AndThatTheSuiteDoesNotCall()
    {
        var controllers = Controllers();
        var called = CalledControllers(SuiteUrls(), controllers);

        Assert.DoesNotContain(OutsideTheSuite.Keys, name => controllers.All(controller => controller.Name != name));
        Assert.DoesNotContain(OutsideTheSuite.Keys, called.Contains);
    }

    [Theory]
    [InlineData("/api/v1/products?pageSize=10", "/api/v1/products", "/api/v1/products")]
    [InlineData("/api/v1/tenants/{A.TenantId}/orders", "/api/v1/tenants/{tenantId:guid}/orders", "/api/v1/tenants/{tenantId:guid}/orders")]
    [InlineData("/api/v1/auth/my-tenant", "/api/v1/auth", "/api/v1/auth/my-tenant")]
    [InlineData("/api/v1/auth/switch-tenant", "/api/v1/auth", "/api/v1/auth")]
    [InlineData("/api/v1/tablesx", "/api/v1/tables", null)]
    [InlineData("/api/v1/files/", "/api/v1/Files/{**key}", "/api/v1/Files/{**key}")]
    public void Url_ShouldCountForTheLongestRouteThatCoversIt(string url, string shortRoute, string? expected)
    {
        var routes = new[] { shortRoute, "/api/v1/auth/my-tenant", "/api/v1/tenants/{tenantId:guid}/orders" }
            .Distinct()
            .Select(route => route.TrimStart('/'))
            .ToList();

        var match = LongestRoute(url, routes);

        Assert.Equal(expected?.TrimStart('/'), match);
    }

    private static HashSet<string> CalledControllers(IEnumerable<string> urls, IReadOnlyList<Type> controllers)
    {
        var actions = controllers
            .SelectMany(controller => ActionRoutesOf(controller).Select(route => (Route: route, Controller: controller.Name)))
            .ToList();
        var routes = actions.Select(action => action.Route).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return urls
            .Select(url => LongestRoute(url, routes))
            .Where(route => route is not null)
            .SelectMany(route => actions.Where(action => string.Equals(action.Route, route, StringComparison.OrdinalIgnoreCase)))
            .Select(action => action.Controller)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The routes of the actions of a controller: its own route followed by the template of each action.</summary>
    private static IEnumerable<string> ActionRoutesOf(Type controller)
    {
        var route = RouteOf(controller);
        return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(action => string.IsNullOrEmpty(action.Template) ? route : $"{route}/{action.Template}")
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The route that covers the URL with the most segments: a value in its parameters can be anything, and a catch-all
    /// parameter (<c>{**key}</c>) takes the rest of the URL, even nothing.
    /// </summary>
    private static string? LongestRoute(string url, IEnumerable<string> routes) => routes
        .Where(route => RoutePattern(route).IsMatch(url))
        .OrderByDescending(route => route.Count(character => character == '/'))
        .FirstOrDefault();

    private static Regex RoutePattern(string route)
    {
        var segments = route.Split('/').Select(segment =>
            segment.StartsWith("{*", StringComparison.Ordinal) ? "[^?#]*"
            : segment.StartsWith('{') ? "[^/?#]+"
            : Regex.Escape(segment));
        return new Regex($"^/{string.Join('/', segments)}(?=[/?#]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static List<Type> Controllers() => typeof(OrdersController).Assembly.GetTypes()
        .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
        .OrderBy(type => type.Name, StringComparer.Ordinal)
        .ToList();

    private static string RouteOf(Type controller)
    {
        var template = controller.GetCustomAttribute<RouteAttribute>()?.Template
            ?? throw new InvalidOperationException($"{controller.Name} has no route of its own.");
        return template.Replace("[controller]", controller.Name[..^"Controller".Length], StringComparison.Ordinal);
    }

    /// <summary>The URLs the suite calls, as written in its source (an interpolated value stays as {expression}).</summary>
    private static List<string> SuiteUrls()
    {
        var tests = Path.Combine(TenantFilterBypassAllowlistTests.FindRepositoryRoot(), "tests", "FoodParkDigital.WebApi.Tests");
        var files = Directory.EnumerateFiles(tests, "CrossTenant*.cs")
            .Where(path => Path.GetFileName(path) != $"{nameof(CrossTenantSuiteCoverageTests)}.cs")
            .Append(Path.Combine(tests, $"{nameof(TwoBusinessesApi)}.cs"));
        return files
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "/api/v1/[^\"\\s]*").Select(match => match.Value))
            .ToList();
    }
}
