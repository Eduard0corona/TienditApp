using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Swagger;

/// <summary>
/// Marks every property that cannot be null as required, so the generated client types say <c>total: number</c>
/// instead of <c>total?: number</c>. Nullable properties (<c>string?</c>, <c>int?</c>) stay optional.
/// </summary>
public sealed class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is not { Count: > 0 })
            return;

        foreach (var (name, property) in schema.Properties)
        {
            if (!property.Nullable)
                schema.Required.Add(name);
        }
    }
}
