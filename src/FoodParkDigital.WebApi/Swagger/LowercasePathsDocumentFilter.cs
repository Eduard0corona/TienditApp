using System.Text.RegularExpressions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Swagger;

/// <summary>
/// Writes the routes as the client calls them ("/api/v1/customers/card", not "/api/v1/Customers/card"): routing
/// ignores case, but the generated client (client/src/api/generated) keys its types by these paths. Route
/// parameters keep their names.
/// </summary>
public sealed partial class LowercasePathsDocumentFilter : IDocumentFilter
{
    [GeneratedRegex(@"\{[^}]+\}|[^{]+")]
    private static partial Regex Segments();

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var paths = new OpenApiPaths();
        foreach (var (path, item) in swaggerDoc.Paths)
            paths[Lowercase(path)] = item;
        swaggerDoc.Paths = paths;
    }

    private static string Lowercase(string path)
        => Segments().Replace(path, match => match.Value.StartsWith('{') ? match.Value : match.Value.ToLowerInvariant());
}
