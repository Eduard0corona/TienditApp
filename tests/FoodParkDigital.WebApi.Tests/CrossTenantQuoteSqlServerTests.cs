using System.Net;
using System.Net.Http.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The quotes through the API (Fase 29C, ADR 0053): a business totals, sends and records the answer of its own quotes only;
/// another business neither lists, opens nor answers them, and a food business without service orders nor agenda has no
/// quotes. The link opens without an account with the customer's name and nothing else of them.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantQuoteSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantQuoteSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task AQuote_ShouldBeOnlyItsBusinesses_AndItsLinkShowOnlyTheCustomersName()
    {
        var mine = await _api.SignUpAsync("cotiza-a");
        var theirs = await _api.SignUpAsync("cotiza-b");
        var kitchen = await _api.SignUpAsync("cotiza-c", TwoBusinessesApi.FoodBusiness);

        var draft = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765", customerEmail = "valeria@correo.mx" }));
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal(("COT-000001", 0), (draft.GetProperty("number").GetString(), draft.GetProperty("status").GetInt32()));

        // The browser sends lines, never a total: the server works it out.
        var saved = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            customerEmail = "valeria@correo.mx",
            validityDays = 15,
            note = "Incluye material",
            lines = new[]
            {
                new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m },
                new { productId = (Guid?)null, description = "Mano de obra", quantity = 2m, unitPrice = 232m }
            },
            total = 1m
        }));
        Assert.Equal(1624m, saved.GetProperty("total").GetDecimal());
        var sent = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/quotes/{id}/send", null));
        var token = sent.GetProperty("trackingToken").GetString()!;

        // The other business does not see it, open it nor answer it; nor does it count among its folios.
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync("/api/v1/quotes?filter=5")))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/quotes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/reply",
            new { accepted = true, channel = 0, response = "Sí" })).StatusCode);
        var theirFirst = await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Ana", customerPhone = "5511112222" }));
        Assert.Equal("COT-000001", theirFirst.GetProperty("number").GetString());
        // A food business has no quotes.
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.Client.GetAsync("/api/v1/quotes")).StatusCode);

        // The link, without an account: the quote and the customer's name, never their phone nor email.
        using var stranger = _api.AnonymousClient();
        var link = await stranger.PostAsJsonAsync("/api/v1/public/quotes/view", new { token });
        var body = await link.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, link.StatusCode);
        Assert.Contains("Valeria Ruiz", body);
        Assert.DoesNotContain("5522107765", body);
        Assert.DoesNotContain("valeria@correo.mx", body);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/view", new { token = new string('x', 43) })).StatusCode);
        // The column compares without case in SQL Server and finds it: the link is the exact one, so another case does not open it.
        var otherCase = new string(token.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        Assert.NotEqual(token, otherCase);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/view", new { token = otherCase })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"/api/v1/quotes/{id}")).StatusCode);

        var accepted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/reply",
            new { accepted = true, channel = 0, response = "Sí, el jueves" }));
        Assert.Equal((2, "Sí, el jueves"), (accepted.GetProperty("status").GetInt32(), accepted.GetProperty("customerResponse").GetString()));
    }

    [SqlServerFact]
    public async Task TheSendsOfAQuote_ShouldBeOnlyItsBusinesses_AndSoTheTemplateOfItsWhatsApp()
    {
        var mine = await _api.SignUpAsync("envia-a");
        var theirs = await _api.SignUpAsync("envia-b");
        var draft = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765", customerEmail = "valeria@correo.mx" }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m } }
        }));
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/quotes/{id}/send", null));

        var email = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/email", new { email = "valeria@correo.mx" }));
        Assert.Equal("valeria@correo.mx", email.GetProperty("to").GetString());

        // The other business does not send it by either channel; and its own quote shows none of these sends.
        Assert.Equal(HttpStatusCode.NotFound,
            (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/email", new { email = "otro@correo.mx" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/whatsapp", new { phone = "5511112222" })).StatusCode);
        var detail = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/quotes/{id}"));
        Assert.Single(detail.GetProperty("emails").EnumerateArray());
        // Without its WhatsApp connected, it says so instead of queuing the message.
        var notConnected = await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/whatsapp", new { phone = "5522107765" });
        Assert.Equal(HttpStatusCode.BadRequest, notConnected.StatusCode);
        Assert.Contains("Conecta el WhatsApp", await notConnected.Content.ReadAsStringAsync());

        // A template of the other business is not one to pick.
        var theirTemplate = await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync("/api/v1/messaging/templates",
            new { name = "Cotización", purpose = 6, body = "Tu cotización {folio}: {enlace}" }));
        var picked = await mine.Client.PutAsJsonAsync("/api/v1/quotes/settings/whatsapp-template",
            new { templateId = theirTemplate.GetProperty("id").GetGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, picked.StatusCode);
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/quotes/settings")))
            .GetProperty("whatsAppTemplates").EnumerateArray());
    }

    [SqlServerFact]
    public async Task TheJobOfAQuote_ShouldBeOnlyItsBusinesses_AndBecomeOneOrderOnce()
    {
        var mine = await _api.SignUpAsync("convierte-a");
        var theirs = await _api.SignUpAsync("convierte-b");
        var draft = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765" }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m } }
        }));
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/quotes/{id}/send", null));
        var accepted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/reply",
            new { accepted = true, channel = 0 }));
        Assert.True(accepted.GetProperty("canConvertToOrder").GetBoolean());

        // The other business does not turn it into an order of its own.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/order", new { })).StatusCode);

        var converted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/order", new { }));
        var orderId = converted.GetProperty("order").GetProperty("id").GetGuid();
        Assert.False(converted.GetProperty("canConvertToOrder").GetBoolean());
        // Its order is the business's, and opens its quote; the other business does not see it.
        var order = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/orders/{orderId}"));
        Assert.Equal((1160m, id), (order.GetProperty("total").GetDecimal(), order.GetProperty("quote").GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
        // Once.
        var again = await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/order", new { });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("ya se convirtió", await again.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task TheJobOfAnOrder_ShouldBeAgendadoOnlyByItsBusiness_AndSeenOnlyByIt()
    {
        var mine = await _api.SignUpAsync("trabajo-a");
        var theirs = await _api.SignUpAsync("trabajo-b");
        var draft = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765" }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Pintar la fachada", quantity = 1m, unitPrice = 9000m } }
        }));
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/quotes/{id}/send", null));
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/reply", new { accepted = true, channel = 0 }));
        var converted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/order", new { }));
        var orderId = converted.GetProperty("order").GetProperty("id").GetGuid();
        Assert.True(converted.GetProperty("canScheduleWork").GetBoolean());
        var startAt = DateTimeOffset.UtcNow.Date.AddDays(7).AddHours(16);
        var job = new { startAt, endAt = startAt.AddHours(8), notes = "Llevar andamio" };

        // The other business does not agenda the job of an order that is not its own.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/work-appointments", job)).StatusCode);

        var scheduled = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/quotes/{id}/work-appointments", job));
        var appointmentId = scheduled.GetProperty("order").GetProperty("workAppointments")[0].GetProperty("id").GetGuid();
        // The appointment and the order that shows it are the business's; the other business sees neither.
        var appointment = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/appointments/{appointmentId}"));
        Assert.Equal(orderId, appointment.GetProperty("workOrderId").GetGuid());
        var order = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/orders/{orderId}"));
        Assert.Equal(appointmentId, order.GetProperty("workAppointments")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/appointments/{appointmentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
    }

    [SqlServerFact]
    public async Task TheCustomer_ShouldAnswerFromTheLink_WithoutAnAccount_OnlyThatQuote_WhileItsBusinessLetsThem()
    {
        var mine = await _api.SignUpAsync("responde-a");
        var theirs = await _api.SignUpAsync("responde-b");
        var (id, token) = await SentQuoteAsync(mine.Client);
        var (theirId, theirToken) = await SentQuoteAsync(theirs.Client);
        using var stranger = _api.AnonymousClient();

        // The customer accepts from the link, without an account; the answer says nothing of them but their name.
        var answer = await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token, accepted = true, response = "Los espero el jueves" });
        var body = await answer.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        var replied = await TwoBusinessesApi.ReadAsync(answer);
        Assert.Equal((2, true, false), (replied.GetProperty("status").GetInt32(), replied.GetProperty("repliedFromLink").GetBoolean(),
            replied.GetProperty("canReply").GetBoolean()));
        Assert.DoesNotContain("5522107765", body);
        // The same answer again changes nothing; another one is refused; a link unknown or of another case is not found.
        Assert.Equal(HttpStatusCode.OK, (await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token, accepted = true })).StatusCode);
        var changed = await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token, accepted = false });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Contains("ya tiene respuesta", await changed.Content.ReadAsStringAsync());
        var otherCase = new string(token.Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)).ToArray());
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = otherCase, accepted = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = new string('x', 43), accepted = true })).StatusCode);
        // An answer that says neither is refused, never read as a rejection.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = theirToken })).StatusCode);

        // The business sees how the customer answered; the other business does not see it, and its own quote did not move.
        var seen = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/quotes/{id}"));
        Assert.Equal((2, 4, "Los espero el jueves"), (seen.GetProperty("status").GetInt32(), seen.GetProperty("replyChannel").GetInt32(),
            seen.GetProperty("customerResponse").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/quotes/{id}")).StatusCode);
        Assert.Equal(1, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"/api/v1/quotes/{theirId}"))).GetProperty("status").GetInt32());
        // Nor does a business record an answer «from the link» itself.
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync($"/api/v1/quotes/{theirId}/reply",
            new { accepted = true, channel = 4 })).StatusCode);

        // A business that turns it off records the answer itself: its link no longer takes one.
        var off = await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync("/api/v1/quotes/settings", new { validityDays = 15, onlineReplies = false }));
        Assert.False(off.GetProperty("onlineReplies").GetBoolean());
        Assert.False((await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/view", new { token = theirToken })))
            .GetProperty("canReply").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = theirToken, accepted = true })).StatusCode);
        Assert.True((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/quotes/settings"))).GetProperty("onlineReplies").GetBoolean());
    }

    /// <summary>A quote of the business for «Cambio de calentador», sent: its id and its link.</summary>
    internal static async Task<(Guid Id, string Token)> SentQuoteAsync(HttpClient client)
    {
        var draft = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/quotes",
            new { customerName = "Valeria Ruiz", customerPhone = "5522107765" }));
        var id = draft.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await client.PutAsJsonAsync($"/api/v1/quotes/{id}", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = "5522107765",
            validityDays = 15,
            lines = new[] { new { productId = (Guid?)null, description = "Cambio de calentador", quantity = 1m, unitPrice = 1160m } }
        }));
        var sent = await TwoBusinessesApi.ReadAsync(await client.PostAsync($"/api/v1/quotes/{id}/send", null));
        return (id, sent.GetProperty("trackingToken").GetString()!);
    }
}
