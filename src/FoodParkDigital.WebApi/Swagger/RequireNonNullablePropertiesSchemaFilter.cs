using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Swagger;

/// <summary>
/// Marks every property that cannot be null as required, so the generated client types say <c>total: number</c>
/// instead of <c>total?: number</c>. Nullable properties (<c>string?</c>, <c>int?</c>, <c>OrderStatus?</c>) stay
/// optional, and so does a request property with <see cref="System.ComponentModel.DefaultValueAttribute"/>: the
/// server uses that value when it is not sent (a tip of 0).
/// </summary>
public sealed class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is not { Count: > 0 })
            return;

        var nullableMembers = NullableMemberNames(context.Type);
        foreach (var (name, property) in schema.Properties)
        {
            // A reference to another schema (an enum, a DTO) cannot say "nullable": the CLR property decides.
            if (!property.Nullable && !nullableMembers.Contains(name))
                schema.Required.Add(name);
        }
    }

    private static HashSet<string> NullableMemberNames(Type type)
    {
        var nullability = new NullabilityInfoContext();
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => Nullable.GetUnderlyingType(p.PropertyType) is not null
                        || p.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>() is not null
                        || (!p.PropertyType.IsValueType && nullability.Create(p).ReadState == NullabilityState.Nullable))
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);
    }
}
