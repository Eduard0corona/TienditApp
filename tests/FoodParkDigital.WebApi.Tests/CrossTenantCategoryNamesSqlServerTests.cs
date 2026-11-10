using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// One name per category in a business, through the API (Fase 31B): creating or editing with a name the business has,
/// whatever its capitals and spaces, answers 400 naming the category that has it; another business can have the name.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantCategoryNamesSqlServerTests
{
    private const string Categories = "/api/v1/categories";

    private readonly TwoBusinessesApi _api;

    public CrossTenantCategoryNamesSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task ANameTheBusinessHas_ShouldBeRefusedWith400_AndAnotherBusinessCanHaveIt()
    {
        var mine = await _api.SignUpAsync("categorias-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("categorias-b", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Categories, new { name = "Especiales de la casa" }));
        var seasonal = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Categories, new { name = "Temporada" }));
        var seasonalId = seasonal.GetProperty("id").GetGuid();

        var created = await mine.Client.PostAsJsonAsync(Categories, new { name = "  especiales DE la   casa " });
        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(CategoryNames.Taken("Especiales de la casa", isActive: true), await DetailAsync(created));

        var edited = await mine.Client.PutAsJsonAsync($"{Categories}/{seasonalId}", new { id = seasonalId, name = "ESPECIALES de la casa" });
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        Assert.Equal(CategoryNames.Taken("Especiales de la casa", isActive: true), await DetailAsync(edited));

        // Another business creates it; mine keeps one of each.
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync(Categories, new { name = "Especiales de la casa" }));
        Assert.Equal(
            ["Especiales de la casa", "Temporada"],
            await _api.QueryAsync(mine.TenantId, db => db.Categories
                .Where(c => c.Name == "Especiales de la casa" || c.Name == "Temporada")
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync()));
        Assert.Equal(
            1,
            await _api.QueryAsync(theirs.TenantId, db => db.Categories.CountAsync(c => c.Name == "Especiales de la casa")));
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
}
