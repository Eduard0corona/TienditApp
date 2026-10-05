using System.Text.Json;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.WebApi.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>The generated client types say which fields always come and which ones may be left out.</summary>
public class RequireNonNullablePropertiesSchemaFilterTests
{
    [Fact]
    public void NonNullableProperties_ShouldBeRequired_AndNullableOnesOptional()
    {
        var schema = GenerateSchema<SampleRequest>();

        Assert.Equal(["items", "name", "status", "total"], schema.Required.Order(StringComparer.Ordinal));
    }

    private static Microsoft.OpenApi.Models.OpenApiSchema GenerateSchema<T>()
    {
        var options = new SchemaGeneratorOptions { SupportNonNullableReferenceTypes = true };
        options.SchemaFilters.Add(new RequireNonNullablePropertiesSchemaFilter());
        var generator = new SchemaGenerator(
            options, new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var repository = new SchemaRepository();

        generator.GenerateSchema(typeof(T), repository);

        return repository.Schemas[typeof(T).Name];
    }

    private sealed record SampleRequest
    {
        public string Name { get; init; } = string.Empty;
        public decimal Total { get; init; }
        public OrderStatus Status { get; init; }
        public List<SampleLine> Items { get; init; } = [];
        public string? Notes { get; init; }
        public int? Limit { get; init; }
        // Enums and DTOs are references to other schemas, which cannot say "nullable" themselves.
        public OrderStatus? PreviousStatus { get; init; }
        public SampleLine? Highlighted { get; init; }
        // The server uses the default when the client leaves it out (a tip of 0).
        [System.ComponentModel.DefaultValue(0)]
        public decimal Tip { get; init; }
    }

    private sealed record SampleLine(string Name);
}
