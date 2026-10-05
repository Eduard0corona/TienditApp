using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Swagger;

/// <summary>
/// Writes the member name of each enum value next to it (<c>x-enum-varnames</c>, the extension code generators read)
/// and marks <see cref="FlagsAttribute"/> enums (<c>x-enum-flags</c>), so the client's named constants are generated
/// from the contract instead of copied by hand. The API keeps exchanging the numbers.
/// </summary>
public sealed class EnumNamesSchemaFilter : ISchemaFilter
{
    public const string NamesExtension = "x-enum-varnames";
    public const string FlagsExtension = "x-enum-flags";

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var type = context.Type;
        if (!type.IsEnum || schema.Enum is not { Count: > 0 })
            return;

        var underlying = Enum.GetUnderlyingType(type);
        var names = new OpenApiArray();
        foreach (var value in schema.Enum)
        {
            long? number = value switch
            {
                OpenApiInteger integer => integer.Value,
                OpenApiLong longValue => longValue.Value,
                _ => null
            };
            var name = number is { } n ? Enum.GetName(type, Convert.ChangeType(n, underlying)) : null;
            // A value without a name (or a string enum) leaves the schema as it was: half a list would mislead.
            if (name is null)
                return;

            names.Add(new OpenApiString(name));
        }

        schema.Extensions[NamesExtension] = names;
        if (type.IsDefined(typeof(FlagsAttribute), inherit: false))
            schema.Extensions[FlagsExtension] = new OpenApiBoolean(true);
    }
}
