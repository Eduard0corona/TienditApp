using System.Net.Http.Json;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Explorar (Fase 24A) through the API: a customer without a session sees each business as it decided, only with what
/// its menu already shows, and only the owners of a business change how it shows (the header of another business
/// changes nothing).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectorySqlServerTests
{
    private const string Directory = "/api/v1/auth/my-tenant/directory";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectorySqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Explorar_ShouldShowEachBusinessAsItDecided_AndOnlyItsOwnersChangeIt()
    {
        var mine = await _api.SignUpAsync("explorar-a", TwoBusinessesApi.FoodBusiness);
        var hidden = await _api.SignUpAsync("explorar-b", TwoBusinessesApi.FoodBusiness);
        var empty = await _api.SignUpAsync("explorar-c", TwoBusinessesApi.FoodBusiness);
        await ProductAsync(mine.Client, "Tacos al pastor");
        await ProductAsync(hidden.Client, "Tortas ahogadas");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        foreach (var owner in new[] { mine, hidden, empty })
            await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(OnlineMenu, new { mode = 2 }));

        var described = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(Directory,
            new { category = 0, description = "Tacos al pastor desde 1998", listed = true }));
        Assert.Equal((int)DirectoryVisibility.Shown, described.GetProperty("visibility").GetInt32());
        await TwoBusinessesApi.ReadAsync(await hidden.Client.PutAsJsonAsync(Directory, new { category = 0, listed = false }));

        // The other business, with the header of mine, changes its own: the header of another business is ignored.
        using (var spoof = new HttpRequestMessage(HttpMethod.Put, Directory)
        {
            Content = JsonContent.Create(new { category = 9, description = "Tortas y aguas frescas", listed = false })
        })
        {
            spoof.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            var own = await TwoBusinessesApi.ReadAsync(await hidden.Client.SendAsync(spoof));
            Assert.Equal((9, (int)DirectoryVisibility.HiddenByOwner), (own.GetProperty("category").GetInt32(), own.GetProperty("visibility").GetInt32()));
        }
        var kept = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Directory));
        Assert.Equal((0, "Tacos al pastor desde 1998", true),
            (kept.GetProperty("category").GetInt32(), kept.GetProperty("description").GetString(), kept.GetProperty("listed").GetBoolean()));
        Assert.Equal((int)DirectoryVisibility.NoProducts,
            (await TwoBusinessesApi.ReadAsync(await empty.Client.GetAsync(Directory))).GetProperty("visibility").GetInt32());

        // A customer without a session finds only mine, taking orders, without its contact.
        _api.ForgetDirectorySnapshot();
        using var customer = _api.AnonymousClient();
        var response = await customer.GetAsync("/api/v1/public/directory?q=explorar");
        var body = await response.Content.ReadAsStringAsync();
        var directory = await TwoBusinessesApi.ReadAsync(response);
        var card = Assert.Single(directory.GetProperty("businesses").GetProperty("items").EnumerateArray());
        Assert.Equal(("Negocio EXPLORAR-A", "Tacos al pastor desde 1998", 0, true),
            (card.GetProperty("name").GetString(), card.GetProperty("description").GetString(),
                card.GetProperty("category").GetInt32(), card.GetProperty("acceptsOrders").GetBoolean()));
        Assert.DoesNotContain(mine.Email, body);
        Assert.DoesNotContain(mine.TenantId.ToString(), body);
        Assert.False(card.TryGetProperty("contactPhone", out _));

        // The change of each owner is recorded in its own business.
        var audit = await _api.QueryAsync(mine.TenantId, db => db.AuditLogs
            .Where(a => a.Action == UpdateDirectoryProfileCommandHandler.AuditAction).ToListAsync());
        Assert.Single(audit);
        Assert.Equal(2, (await _api.QueryAsync(hidden.TenantId, db => db.AuditLogs
            .Where(a => a.Action == UpdateDirectoryProfileCommandHandler.AuditAction).ToListAsync())).Count);
    }
}
