using System.Text.Json;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Swagger;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>The contract names each enum value, so the client's constants are generated instead of copied.</summary>
public class EnumNamesSchemaFilterTests
{
    [Fact]
    public void EnumValues_ShouldCarryTheirNamesInTheirOrder()
    {
        var schema = GenerateSchema<InventoryMovementType>();

        var names = Assert.IsType<OpenApiArray>(schema.Extensions[EnumNamesSchemaFilter.NamesExtension]);
        Assert.Equal(
            Enum.GetNames<InventoryMovementType>(),
            names.Cast<OpenApiString>().Select(name => name.Value));
        Assert.Equal(
            Enum.GetValues<InventoryMovementType>().Select(value => (int)value),
            schema.Enum.Cast<OpenApiInteger>().Select(value => value.Value));
        Assert.False(schema.Extensions.ContainsKey(EnumNamesSchemaFilter.FlagsExtension));
    }

    [Fact]
    public void FlagsEnums_ShouldBeMarked()
    {
        var schema = GenerateSchema<TenantCapability>();

        Assert.True(Assert.IsType<OpenApiBoolean>(schema.Extensions[EnumNamesSchemaFilter.FlagsExtension]).Value);
        Assert.Contains(
            nameof(TenantCapability.Kitchen),
            Assert.IsType<OpenApiArray>(schema.Extensions[EnumNamesSchemaFilter.NamesExtension]).Cast<OpenApiString>().Select(name => name.Value));
    }

    private static OpenApiSchema GenerateSchema<T>()
    {
        var options = new SchemaGeneratorOptions();
        options.SchemaFilters.Add(new EnumNamesSchemaFilter());
        var generator = new SchemaGenerator(
            options, new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var repository = new SchemaRepository();

        generator.GenerateSchema(typeof(T), repository);

        return repository.Schemas[typeof(T).Name];
    }
}
