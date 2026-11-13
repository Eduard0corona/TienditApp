using System.Net;
using System.Net.Http.Json;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Fase 31K (ADR 0091), through the API, the messaging worker's pass and the outbox: each owner is reminded once by email of
/// the orders of their own online menu still waiting to be accepted 15 minutes after they arrived, never of another
/// business's nor of one answered or of a suspended business; and each owner hears of each new review of their own business
/// only, which only owners and admins choose.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantOnlineOrderAndReviewEmailsSqlServerTests
{
    private const string ReviewEmails = "/api/v1/reviews/emails";

    private readonly TwoBusinessesApi _api;

    public CrossTenantOnlineOrderAndReviewEmailsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task EachOwner_ShouldBeRemindedOnce_OfTheOrdersOfTheirOwnMenuStillWaiting()
    {
        var mine = await _api.SignUpAsync("espera-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("espera-b", TwoBusinessesApi.FoodBusiness);
        var suspended = await _api.SignUpAsync("espera-c", TwoBusinessesApi.FoodBusiness);
        var myKitchen = await KitchenAsync(mine);
        var theirKitchen = await KitchenAsync(theirs);
        var suspendedKitchen = await KitchenAsync(suspended);
        using var stranger = _api.AnonymousClient();

        // Each kitchen gets an order that has waited 16 minutes; mine also one accepted and one that arrived 10 minutes ago.
        var myWaiting = await OrderAsync(stranger, myKitchen, "5522107801", minutesAgo: 16);
        var myAccepted = await OrderAsync(stranger, myKitchen, "5522107802", minutesAgo: 16);
        var myRecent = await OrderAsync(stranger, myKitchen, "5522107803", minutesAgo: 10);
        var theirWaiting = await OrderAsync(stranger, theirKitchen, "5522107804", minutesAgo: 16);
        var suspendedWaiting = await OrderAsync(stranger, suspendedKitchen, "5522107805", minutesAgo: 16);
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.PutAsJsonAsync($"/api/v1/orders/{myAccepted}/status", new { newStatus = 1 })).StatusCode);
        await _api.QueryAsync(suspended.TenantId, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Tenants SET Status = {(int)TenantStatus.Suspended} WHERE Id = {suspended.TenantId}"));
        await _api.DispatchOutboxAsync();
        var mineBefore = _api.PlainEmailsTo(mine.Email).Count;
        var theirsBefore = _api.PlainEmailsTo(theirs.Email).Count;

        Assert.True(await _api.RemindOnlineOrdersAsync() >= 2);
        await _api.DispatchOutboxAsync();

        var myReminder = Assert.Single(_api.PlainEmailsTo(mine.Email).Skip(mineBefore));
        Assert.StartsWith("Pedido en línea por confirmar en Negocio ESPERA-A: ORD-", myReminder.Subject);
        Assert.DoesNotContain("5522107801", myReminder.Subject + myReminder.Body);
        Assert.DoesNotContain("Tacos", myReminder.Body);
        Assert.StartsWith("Pedido en línea por confirmar en Negocio ESPERA-B",
            Assert.Single(_api.PlainEmailsTo(theirs.Email).Skip(theirsBefore)).Subject);
        Assert.Equal(
            (true, false, false),
            (await RemindedAsync(mine.TenantId, myWaiting), await RemindedAsync(mine.TenantId, myAccepted), await RemindedAsync(mine.TenantId, myRecent)));
        Assert.True(await RemindedAsync(theirs.TenantId, theirWaiting));
        Assert.False(await RemindedAsync(suspended.TenantId, suspendedWaiting));

        // Once: another pass reminds them of nothing more.
        await _api.RemindOnlineOrdersAsync();
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email).Skip(mineBefore));
        Assert.Single(_api.PlainEmailsTo(theirs.Email).Skip(theirsBefore));
    }

    [SqlServerFact]
    public async Task EachOwner_ShouldHearOfTheNewReviewsOfTheirOwnBusiness_WhichOnlyOwnersAndAdminsChoose()
    {
        var mine = await _api.SignUpAsync("resena-aviso-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("resena-aviso-b", TwoBusinessesApi.FoodBusiness);
        var kitchen = await KitchenAsync(mine);
        await KitchenAsync(theirs);
        var ana = await _api.SignUpCustomerAsync("resena-aviso-ana");

        // The choice is for whoever answers the reviews: on for the owner until they choose, never for the rest of the team.
        var owners = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(ReviewEmails));
        Assert.Equal((true, mine.Email), (owners.GetProperty("enabled").GetBoolean(), owners.GetProperty("email").GetString()));
        var cashier = await CashierAsync(mine);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(ReviewEmails)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync(ReviewEmails, new { enabled = true })).StatusCode);

        // Ana orders with her session, receives it and writes a review.
        await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync($"/api/v1/public/menu/{kitchen.Slug}/orders", new
        {
            customerName = "Ana Cliente",
            customerPhone = "5512345601",
            items = new[] { new { productId = kitchen.Product, quantity = 1 } },
            idempotencyKey = Guid.NewGuid()
        }));
        await _api.QueryAsync(mine.TenantId, db => db.Orders.Where(o => o.CustomerUserId == ana.UserId)
            .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, OrderStatus.Delivered)));
        await _api.DispatchOutboxAsync();
        var mineBefore = _api.PlainEmailsTo(mine.Email).Count;
        var theirsBefore = _api.PlainEmailsTo(theirs.Email).Count;
        await TwoBusinessesApi.ReadAsync(await ana.Client.PutAsJsonAsync($"/api/v1/me/reviews/{kitchen.Slug}", new { rating = 4, comment = "Tacos <ricos>" }));
        await _api.DispatchOutboxAsync();

        var told = Assert.Single(_api.PlainEmailsTo(mine.Email).Skip(mineBefore));
        Assert.Equal("Nueva reseña en Negocio RESENA-AVISO-A: 4 estrellas de 5", told.Subject);
        Assert.Contains("Tacos &lt;ricos&gt;", told.Body);
        Assert.DoesNotContain(ana.Email, told.Body);
        Assert.Empty(_api.PlainEmailsTo(theirs.Email).Skip(theirsBefore));

        // Changing it tells nothing again; and the owner turns them off for themselves.
        await TwoBusinessesApi.ReadAsync(await ana.Client.PutAsJsonAsync($"/api/v1/me/reviews/{kitchen.Slug}", new { rating = 2, comment = "Ya no tanto" }));
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email).Skip(mineBefore));
        Assert.False((await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(ReviewEmails, new { enabled = false }))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(ReviewEmails))).GetProperty("enabled").GetBoolean());
    }

    [SqlServerFact]
    public async Task APhotoOrAudioOfWhatsApp_ShouldOpenOnlyForWhoRunsTheChatOfItsBusiness()
    {
        // Fase 31L (ADR 0092): kept by the outbox in the business's folder, opened through the files endpoint.
        var mine = await _api.SignUpAsync("medios-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("medios-b", TwoBusinessesApi.FoodBusiness);
        var key = $"whatsapp/{mine.TenantId}/202611/{Guid.NewGuid()}.ogg";
        await _api.PrivateFiles.SaveAsync(key, new MemoryStream("OggS\0\u0002"u8.ToArray()), "audio/ogg");
        var cashier = await CashierAsync(mine);

        var opened = await mine.Client.GetAsync($"/api/v1/files/{key}");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("audio/ogg", opened.Content.Headers.ContentType?.MediaType);
        Assert.True(opened.Headers.CacheControl is { Private: true, NoStore: true }, opened.Headers.CacheControl?.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/files/{key}")).StatusCode);
        // Whoever does not run the chat (a cashier without the WhatsApp permission) does not open it.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/files/{key}")).StatusCode);
        // A key the app never creates is like a missing one.
        Assert.Equal(HttpStatusCode.NotFound, (await mine.Client.GetAsync($"/api/v1/files/whatsapp/{mine.TenantId}/202611/x.exe")).StatusCode);
    }

    /// <summary>The owner's kitchen taking online orders, with its drawer open: its slug and a product.</summary>
    private static async Task<(SignedUp Owner, string Slug, Guid Product)> KitchenAsync(SignedUp owner)
    {
        var product = await CrossTenantManagementSqlServerTests.ProductAsync(owner.Client, "Tacos al pastor");
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 2 })))
            .GetProperty("slug").GetString()!;
        return (owner, slug, product);
    }

    /// <summary>An order of the online menu that arrived <paramref name="minutesAgo"/>, still waiting: its id.</summary>
    private async Task<Guid> OrderAsync(HttpClient customer, (SignedUp Owner, string Slug, Guid Product) kitchen, string phone, int minutesAgo)
    {
        var token = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{kitchen.Slug}/orders", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = phone,
            items = new[] { new { productId = kitchen.Product, quantity = 1 } }
        }))).GetProperty("trackingToken").GetString()!;
        var id = await _api.QueryAsync(kitchen.Owner.TenantId, db => db.Orders.Where(o => o.TrackingToken == token).Select(o => o.Id).SingleAsync());
        await _api.QueryAsync(kitchen.Owner.TenantId, db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Orders SET CreatedAt = DATEADD(minute, {-minutesAgo}, SYSDATETIMEOFFSET()) WHERE Id = {id}"));
        return id;
    }

    /// <summary>A cashier of the business, signed in: they confirm the orders but do not answer the reviews.</summary>
    private async Task<HttpClient> CashierAsync(SignedUp owner)
    {
        var email = $"cajero-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = "Secreto1",
            firstName = "Cajero",
            lastName = "De reseñas",
            appRole = 2,
            tenantRole = 2,
            permissions = new[] { "ManageOrders" }
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Secreto1" }));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", signedIn.GetProperty("accessToken").GetString()!);
        return client;
    }

    private Task<bool> RemindedAsync(Guid tenantId, Guid order)
        => _api.QueryAsync(tenantId, db => db.Orders.AsNoTracking().AnyAsync(o => o.Id == order && o.ConfirmReminderSentAt != null));
}
