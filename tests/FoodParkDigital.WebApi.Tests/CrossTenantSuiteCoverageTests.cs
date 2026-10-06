using System.Reflection;
using System.Text.RegularExpressions;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Every controller with data of a business is called by the suite between two businesses (the CrossTenant* classes and
/// <see cref="TwoBusinessesApi"/>): a new one fails here until a test of the suite calls its routes with what belongs to
/// the other business. Each URL of the suite counts for the controller with the longest route that covers it, so
/// /api/v1/auth/my-tenant does not count for the route /api/v1/auth. The ones outside on purpose say why.
/// </summary>
public class CrossTenantSuiteCoverageTests
{
    private static readonly Dictionary<string, string> OutsideTheSuite = new(StringComparer.Ordinal)
    {
        [nameof(PlatformController)] = "La consola de plataforma trabaja entre negocios por diseño y es solo para SUPER_ADMIN.",
        [nameof(PublicMenuController)] = "El menú público es anónimo: el negocio sale del slug de su ruta.",
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

        Assert.Empty(OutsideTheSuite.Keys.Where(name => controllers.All(controller => controller.Name != name)));
        Assert.Empty(OutsideTheSuite.Keys.Where(called.Contains));
    }

    [Theory]
    [InlineData("/api/v1/products?pageSize=10", "/api/v1/products", "/api/v1/products")]
    [InlineData("/api/v1/tenants/{A.TenantId}/orders", "/api/v1/tenants/{tenantId:guid}/orders", "/api/v1/tenants/{tenantId:guid}/orders")]
    [InlineData("/api/v1/auth/my-tenant", "/api/v1/auth", "/api/v1/auth/my-tenant")]
    [InlineData("/api/v1/auth/switch-tenant", "/api/v1/auth", "/api/v1/auth")]
    [InlineData("/api/v1/tablesx", "/api/v1/tables", null)]
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
        var byRoute = controllers.ToDictionary(RouteOf, controller => controller.Name, StringComparer.OrdinalIgnoreCase);
        return urls
            .Select(url => LongestRoute(url, byRoute.Keys))
            .Where(route => route is not null)
            .Select(route => byRoute[route!])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The route that covers the URL with the most segments: a value in its parameters can be anything.</summary>
    private static string? LongestRoute(string url, IEnumerable<string> routes) => routes
        .Where(route => RoutePattern(route).IsMatch(url))
        .OrderByDescending(route => route.Count(character => character == '/'))
        .FirstOrDefault();

    private static Regex RoutePattern(string route)
    {
        var segments = route.Split('/').Select(segment =>
            segment.StartsWith('{') ? "[^/?#]+" : Regex.Escape(segment));
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
