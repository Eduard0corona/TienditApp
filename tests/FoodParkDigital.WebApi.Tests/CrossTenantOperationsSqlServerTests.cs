using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using static FoodParkDigital.WebApi.Tests.CrossTenantHttpSqlServerTests;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// What a business works with day to day: Inicio, the kitchen, the service board and its photos, and the automatic replies. One
/// business tries to read, move and change the other's by their ids: every attempt fails as if they did not exist,
/// nothing of the other business changes, and none of it shows or counts on its own screens. Each case also does the
/// same with its own records, so the refusals are about the business and not about the request. Runs where
/// TIENDITAPP_TEST_SQLSERVER names a server, like <see cref="CrossTenantHttpSqlServerTests"/>.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantOperationsSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantOperationsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    private Business B => _api.B;

    [SqlServerFact]
    public async Task Home_ShouldShowAndCountOnlyTheirOwnBusiness()
    {
        var metricsBefore = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/metrics");
        var attentionBefore = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/attention");

        // Another business sells and gets paid, takes a service it is already late with, runs low on a supply and has
        // a chat waiting.
        var c = await _api.SignUpAsync("inicio");
        var product = await ProductAsync(c.Client, "Corte de inicio");
        var paid = await PaidSaleAsync(c.Client, product, phone: null, name: "Mostrador");
        var service = await ServiceOrderAsync(c.Client, product, "Bota de inicio", promisedAt: DateTimeOffset.UtcNow.AddDays(-1));
        await LowSupplyAsync(c.Client, "Tinte de inicio");
        await ChatWaitingAsync(c.Client);

        // Its Inicio shows all of it...
        var metrics = await c.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/metrics");
        Assert.Equal(2, metrics.GetProperty("totalOrders").GetInt32());
        Assert.Equal(paid, metrics.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(1, metrics.GetProperty("paidOrders").GetInt32());
        Assert.Equal(1, metrics.GetProperty("unpaidOrders").GetInt32());
        Assert.Equal(service.GetProperty("total").GetDecimal(), metrics.GetProperty("outstandingBalance").GetDecimal());
        Assert.Equal(1, metrics.GetProperty("activeProducts").GetInt32());
        Assert.Equal("Corte de inicio", Assert.Single(metrics.GetProperty("topSellingItems").EnumerateArray()).GetProperty("productName").GetString());
        var attention = AttentionCounts(await c.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/attention"));
        Assert.Equal(1, attention.GetValueOrDefault("lowStock"));
        Assert.Equal(1, attention.GetValueOrDefault("lateServices"));
        Assert.Equal(1, attention.GetValueOrDefault("unreadChats"));

        // ...and B's did not move.
        var metricsAfter = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/metrics");
        foreach (var figure in new[]
        {
            "totalOrders", "inProgressOrders", "pendingOrders", "completedOrders", "cancelledOrders", "uniqueCustomers",
            "activeProducts", "activeServices", "totalRevenue", "paidOrders", "averageOrderValue", "outstandingBalance",
            "unpaidOrders", "ordersByStatus", "topSellingItems"
        })
        {
            Assert.Equal(metricsBefore.GetProperty(figure).GetRawText(), metricsAfter.GetProperty(figure).GetRawText());
        }

        var attentionAfter = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/attention");
        Assert.Equal(attentionBefore.GetRawText(), attentionAfter.GetRawText());
    }

    [SqlServerFact]
    public async Task Kitchen_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        // The kitchen is for food businesses: two of them.
        var mine = await _api.SignUpAsync("cocina-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("cocina-b", TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(mine.Client, "Taco de cocina A");
        var theirProduct = await ProductAsync(theirs.Client, "Taco de cocina B");
        // A counter order and the first round of a tab, both waiting in A's kitchen.
        var order = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/orders", CounterOrder(product));
        var tab = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/orders", Tab(product, "Barra de A"));
        var ticket = (await TicketOfAsync(mine.Client, tab)).GetProperty("id").GetGuid();
        var orderBefore = await StatusOfOrderAsync(mine.Client, order);

        foreach (var step in new[] { "start", "ready" })
            AssertRefused(await theirs.Client.PutAsync($"/api/v1/kitchen/orders/{order}/{step}", null));
        foreach (var step in new[] { "start", "ready", "served" })
            AssertRefused(await theirs.Client.PutAsync($"/api/v1/kitchen/tickets/{ticket}/{step}", null));
        // The waiter's "taken to the table".
        AssertRefused(await theirs.Client.PutAsync($"/api/v1/orders/kitchen-tickets/{ticket}/served", null));
        foreach (var list in new[] { "/api/v1/kitchen/orders", "/api/v1/kitchen/tickets" })
        {
            var theirKitchen = await theirs.Client.GetStringAsync(list);
            Assert.DoesNotContain(order.ToString(), theirKitchen);
            Assert.DoesNotContain(tab.ToString(), theirKitchen);
            Assert.DoesNotContain(ticket.ToString(), theirKitchen);
        }

        // Their own kitchen moves.
        var theirOrder = await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/orders", CounterOrder(theirProduct));
        var theirTab = await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/orders", Tab(theirProduct, "Barra de B"));
        var theirTicket = (await TicketOfAsync(theirs.Client, theirTab)).GetProperty("id").GetGuid();
        await AssertNoContentAsync(theirs.Client.PutAsync($"/api/v1/kitchen/orders/{theirOrder}/start", null));
        await AssertNoContentAsync(theirs.Client.PutAsync($"/api/v1/kitchen/tickets/{theirTicket}/start", null));

        // A's kitchen is as it was.
        Assert.Equal(orderBefore, await StatusOfOrderAsync(mine.Client, order));
        Assert.Equal(0, (await TicketOfAsync(mine.Client, tab)).GetProperty("status").GetInt32());
    }

    [SqlServerFact]
    public async Task ServiceBoard_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var mine = await _api.SignUpAsync("taller-a");
        var theirs = await _api.SignUpAsync("taller-b");
        var service = await ServiceOrderAsync(
            mine.Client, await ProductAsync(mine.Client, "Reparación de A"), "Bota de A", promisedAt: DateTimeOffset.UtcNow.AddDays(1));
        var order = service.GetProperty("id").GetGuid();
        var piece = Assert.Single(service.GetProperty("servicePieces").EnumerateArray()).GetProperty("id").GetGuid();
        // Taken by A's owner: a manager of another business would otherwise take it over or release it.
        await AssertNoContentAsync(mine.Client.PutAsync($"/api/v1/service-board/pieces/{piece}/assign-to-me", null));
        var before = await PieceAsync(mine.Client, piece);

        AssertRefused(await theirs.Client.PutAsync($"/api/v1/service-board/pieces/{piece}/assign-to-me", null));
        AssertRefused(await theirs.Client.PutAsync($"/api/v1/service-board/pieces/{piece}/release", null));
        AssertRefused(await theirs.Client.PutAsJsonAsync($"/api/v1/orders/{order}/service-pieces/{piece}/status", new { newStatus = 1 }));
        var theirBoard = await theirs.Client.GetStringAsync("/api/v1/service-board/pieces?includeCompleted=true&includeCancelled=true&pageSize=100");
        Assert.DoesNotContain(piece.ToString(), theirBoard);
        Assert.DoesNotContain("Bota de A", theirBoard);

        // Their own board moves.
        var theirService = await ServiceOrderAsync(
            theirs.Client, await ProductAsync(theirs.Client, "Reparación de B"), "Bota de B", promisedAt: DateTimeOffset.UtcNow.AddDays(1));
        var theirPiece = theirService.GetProperty("servicePieces")[0].GetProperty("id").GetGuid();
        await AssertNoContentAsync(theirs.Client.PutAsync($"/api/v1/service-board/pieces/{theirPiece}/assign-to-me", null));
        await AssertNoContentAsync(theirs.Client.PutAsync($"/api/v1/service-board/pieces/{theirPiece}/release", null));

        var stillThere = await PieceAsync(mine.Client, piece);
        foreach (var field in new[] { "status", "assignedToUserId", "assignedToName", "assignedAt" })
            Assert.Equal(before.GetProperty(field).GetRawText(), stillThere.GetProperty(field).GetRawText());
    }

    [SqlServerFact]
    public async Task ServicePhotos_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var mine = await _api.SignUpAsync("fotos-a");
        var theirs = await _api.SignUpAsync("fotos-b");
        var order = (await ServiceOrderAsync(
            mine.Client, await ProductAsync(mine.Client, "Lavado de A"), "Saco de A", promisedAt: DateTimeOffset.UtcNow.AddDays(1)))
            .GetProperty("id").GetGuid();
        var myFile = await UploadServicePhotoAsync(mine.Client);
        var photo = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/orders/{order}/service-photos", new { stage = 0, fileUrl = myFile, note = "Mancha de A" })))
            .GetProperty("id").GetGuid();
        var before = await mine.Client.GetStringAsync($"/api/v1/orders/{order}/service-photos");

        AssertRefused(await theirs.Client.GetAsync($"/api/v1/orders/{order}/service-photos"));
        AssertRefused(await theirs.Client.PostAsJsonAsync($"/api/v1/orders/{order}/service-photos", new { stage = 1, fileUrl = myFile }));
        AssertRefused(await theirs.Client.DeleteAsync($"/api/v1/orders/{order}/service-photos/{photo}"));
        AssertRefused(await theirs.Client.GetAsync(myFile.Replace("/files/", "/api/v1/files/")));

        // Their own order takes their own photos, and not the file of another business.
        var theirOrder = (await ServiceOrderAsync(
            theirs.Client, await ProductAsync(theirs.Client, "Lavado de B"), "Saco de B", promisedAt: DateTimeOffset.UtcNow.AddDays(1)))
            .GetProperty("id").GetGuid();
        AssertRefused(await theirs.Client.PostAsJsonAsync($"/api/v1/orders/{theirOrder}/service-photos", new { stage = 0, fileUrl = myFile }));
        var theirPhoto = (await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync(
            $"/api/v1/orders/{theirOrder}/service-photos", new { stage = 0, fileUrl = await UploadServicePhotoAsync(theirs.Client) })))
            .GetProperty("id").GetGuid();
        await AssertNoContentAsync(theirs.Client.DeleteAsync($"/api/v1/orders/{theirOrder}/service-photos/{theirPhoto}"));

        Assert.Equal(before, await mine.Client.GetStringAsync($"/api/v1/orders/{order}/service-photos"));
        Assert.Contains("Mancha de A", before);
    }

    [SqlServerFact]
    public async Task AiAgents_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        // Fresh businesses: an active agent answers the chats of its business, and the shared ones receive messages.
        var mine = await _api.SignUpAsync("agentes-a");
        var theirs = await _api.SignUpAsync("agentes-b");
        var agent = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/ai-agents", new
        {
            name = "Asistente",
            role = "Ventas",
            businessDescription = "Lo que solo sabe A"
        });
        var path = $"/api/v1/ai-agents/{agent}";
        var before = await mine.Client.GetFromJsonAsync<JsonElement>(path);

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(path)).StatusCode);
        AssertRefused(await theirs.Client.PutAsJsonAsync(path, AgentChanges(agent, "Cambiado por B")));
        AssertRefused(await theirs.Client.PatchAsJsonAsync($"{path}/active", new { isActive = false }));
        AssertRefused(await theirs.Client.DeleteAsync(path));
        foreach (var list in new[] { "/api/v1/ai-agents", "/api/v1/ai-agents?isActive=true" })
        {
            var theirAgents = await theirs.Client.GetStringAsync(list);
            Assert.DoesNotContain(agent.ToString(), theirAgents);
            Assert.DoesNotContain("Lo que solo sabe A", theirAgents);
        }

        // Their own agents, by the same name: it tells nothing of the other business.
        var theirAgent = await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/ai-agents", new { name = "Asistente", role = "Ventas" });
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync($"/api/v1/ai-agents/{theirAgent}", AgentChanges(theirAgent, "Asistente de B")));
        await AssertNoContentAsync(theirs.Client.PatchAsJsonAsync($"/api/v1/ai-agents/{theirAgent}/active", new { isActive = false }));
        await AssertNoContentAsync(theirs.Client.DeleteAsync($"/api/v1/ai-agents/{theirAgent}"));

        var stillThere = await mine.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(before.GetRawText(), stillThere.GetRawText());
    }

    private static object CounterOrder(Guid product) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = false,
        customerName = "Mostrador",
        items = new[] { Line(product) },
        payments = Array.Empty<object>()
    };

    private static object Tab(Guid product, string tabName) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = true,
        tableNumber = tabName,
        customerName = "Mesa",
        items = new[] { Line(product) },
        payments = Array.Empty<object>()
    };

    /// <returns>The order as created, with its pieces: the one described, to be delivered at the given time.</returns>
    private static async Task<JsonElement> ServiceOrderAsync(HttpClient client, Guid product, string piece, DateTimeOffset promisedAt)
        => await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 1,
            channel = 0,
            createdByBusiness = true,
            customerName = "Cliente del taller",
            items = new[] { Line(product) },
            payments = Array.Empty<object>(),
            serviceDetail = new
            {
                receivedAt = promisedAt.AddDays(-1),
                promisedAt,
                priority = 3,
                pieces = new[] { new { itemName = piece } }
            }
        }));

    /// <summary>A supply with 1 in stock and its reorder point at 5.</summary>
    private static async Task LowSupplyAsync(HttpClient client, string name)
    {
        var locations = await client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/locations");
        var location = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var supply = await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/supplies", new { name, unit = 0, defaultCost = 10m, reorderPoint = 5m });
        await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = location, quantity = 1m, unitCost = 10m } }
        });
    }

    /// <summary>Gives the business a WhatsApp number (disabled: nothing goes to Meta) and a customer's message to it.</summary>
    private async Task ChatWaitingAsync(HttpClient client)
    {
        var phoneNumberId = $"3{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        await TwoBusinessesApi.ReadAsync(await client.PutAsJsonAsync("/api/v1/auth/my-tenant/whatsapp", new
        {
            isEnabled = false,
            phoneNumberId,
            displayPhoneNumber = "+52 55 0000 0004"
        }));
        var delivered = await _api.PostWebhookAsync(TwoBusinessesApi.InboundMessage(phoneNumberId, "5215577778888", "Hola, ¿ya está mi bota?"));
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        Assert.NotEqual(0, await _api.ProcessWebhookInboxAsync());
    }

    private static Dictionary<string, int> AttentionCounts(JsonElement attention)
        => attention.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("key").GetString()!, item => item.GetProperty("count").GetInt32());

    private static async Task<int> StatusOfOrderAsync(HttpClient client, Guid order)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{order}")).GetProperty("status").GetInt32();

    private static async Task<JsonElement> TicketOfAsync(HttpClient client, Guid tab)
    {
        var tickets = await client.GetFromJsonAsync<JsonElement>("/api/v1/kitchen/tickets");
        return tickets.EnumerateArray().Single(t => t.GetProperty("orderId").GetGuid() == tab);
    }

    private static async Task<JsonElement> PieceAsync(HttpClient client, Guid piece)
    {
        var board = await client.GetFromJsonAsync<JsonElement>("/api/v1/service-board/pieces?includeCompleted=true&includeCancelled=true&pageSize=100");
        return board.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("pieceId").GetGuid() == piece);
    }

    /// <summary>Every field of an agent, as the screen sends them: valid, so only the business can refuse it.</summary>
    private static object AgentChanges(Guid agent, string name) => new
    {
        id = agent,
        name,
        role = "Atención",
        personality = "Directo",
        language = "Espanol",
        tone = "Serio",
        businessDescription = "Otra descripción",
        welcomeMessage = "Hola",
        fallbackMessage = "No sé",
        closingMessage = "Adiós",
        askForName = false,
        askForPhone = false,
        model = "gpt-4.1-mini",
        temperature = 0.2m,
        maxTokens = 500,
        useConversationMemory = false,
        strictMode = true
    };

    /// <summary>A photo of a service uploaded by the business: the "/files/{key}" it keeps.</summary>
    private static async Task<string> UploadServicePhotoAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var png = new ByteArrayContent(TwoBusinessesApi.TinyPng);
        png.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(png, "file", "saco.png");
        return (await TwoBusinessesApi.ReadAsync(await client.PostAsync("/api/v1/orders/uploads/service-photo", form)))
            .GetProperty("url").GetString()!;
    }

    private static async Task AssertNoContentAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}: "
            + await response.Content.ReadAsStringAsync());
    }
}
