using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.SignalR.Client;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The live channels of the two businesses on the real API (SQL Server): hub events reach only the business they
/// belong to (and a request online, the emails of its own business only), a hub connection closes when its session ends,
/// and each WhatsApp delivery reaches only the business that owns its number. Runs where TIENDITAPP_TEST_SQLSERVER names
/// a server (the Migrations job of CI).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantRealtimeSqlServerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private readonly TwoBusinessesApi _api;

    public CrossTenantRealtimeSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    private Business A => _api.A;
    private Business B => _api.B;

    [SqlServerFact]
    public async Task OrderEvents_ShouldReachOnlyTheirBusiness()
    {
        var seenByA = new Events();
        var seenByB = new Events();
        await using var hubA = await ConnectAsync("/hubs/orders", A.AccessToken, "OrderCreated", seenByA, OrderIdOf);
        await using var hubB = await ConnectAsync("/hubs/orders", B.AccessToken, "OrderCreated", seenByB, OrderIdOf);

        // B's connection is live (its own sale reaches it) before A sells. Then B sells again: had A's sale been sent
        // to B, it would have arrived before that one.
        var firstOfB = await _api.CreateOrderAsync(B);
        await seenByB.WaitForAsync(firstOfB.ToString());
        var ofA = await _api.CreateOrderAsync(A);
        await seenByA.WaitForAsync(ofA.ToString());
        var secondOfB = await _api.CreateOrderAsync(B);
        await seenByB.WaitForAsync(secondOfB.ToString());

        Assert.False(seenByB.Saw(ofA.ToString()));
        Assert.False(seenByA.Saw(firstOfB.ToString()));
        Assert.False(seenByA.Saw(secondOfB.ToString()));
    }

    [SqlServerFact]
    public async Task HubConnection_ShouldClose_WhenItsSessionEnds_WhileOtherSessionsKeepTheirs()
    {
        var owner = await _api.SignUpAsync("c");
        using var ownerClient = owner.Client;
        await using var hub = _api.Hub("/hubs/orders", owner.AccessToken);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await hub.StartAsync();
        var seenByA = new Events();
        await using var hubA = await ConnectAsync("/hubs/orders", A.AccessToken, "OrderCreated", seenByA, OrderIdOf);

        var signedOut = await _api.SignOutAsync(owner);
        Assert.True(signedOut.IsSuccessStatusCode, $"logout answered {(int)signedOut.StatusCode}");

        // Over WebSockets nothing else checks the token again: the access sweep (every second here) closes it.
        await closed.Task.WaitAsync(Patience);
        Assert.Equal(HubConnectionState.Disconnected, hub.State);
        // The ended session cannot connect again either.
        await using var again = _api.Hub("/hubs/orders", owner.AccessToken);
        await Assert.ThrowsAnyAsync<Exception>(() => again.StartAsync());

        // A session still open keeps its connection through the sweeps, and its events.
        await Task.Delay(TimeSpan.FromSeconds(2));
        var order = await _api.CreateOrderAsync(A);
        await seenByA.WaitForAsync(order.ToString());
        Assert.Equal(HubConnectionState.Connected, hubA.State);
    }

    [SqlServerFact]
    public async Task RequestsOnline_ShouldReachOnlyTheirBusiness_OnItsScreensAndByEmailToWhoGetsThem()
    {
        // Fase 29N (ADR 0068).
        var mine = await _api.SignUpAsync("avisos-a");
        var theirs = await _api.SignUpAsync("avisos-b");
        var (mySlug, myService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte de avisos");
        var (theirSlug, theirService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(theirs.Client, "Masaje de avisos");
        var seenByMine = new Events();
        var seenByTheirs = new Events();
        await using var hubMine = await ConnectAsync("/hubs/appointments", mine.AccessToken, "AppointmentRequested", seenByMine, AppointmentIdOf);
        await using var hubTheirs = await ConnectAsync("/hubs/appointments", theirs.AccessToken, "AppointmentRequested", seenByTheirs, AppointmentIdOf);
        using var stranger = _api.AnonymousClient();

        // Theirs is live (its own request reaches it) before mine gets one; then theirs gets another: had mine been sent
        // to theirs, it would have arrived before that one.
        var firstOfTheirs = await RequestAsync(stranger, theirSlug, theirService, "5512340001");
        await seenByTheirs.WaitForAsync(firstOfTheirs.ToString());
        var ofMine = await RequestAsync(stranger, mySlug, myService, "5512340002");
        await seenByMine.WaitForAsync(ofMine.ToString());
        var secondOfTheirs = await RequestAsync(stranger, theirSlug, theirService, "5512340003");
        await seenByTheirs.WaitForAsync(secondOfTheirs.ToString());
        Assert.False(seenByTheirs.Saw(ofMine.ToString()));
        Assert.False(seenByMine.Saw(firstOfTheirs.ToString()));
        Assert.False(seenByMine.Saw(secondOfTheirs.ToString()));

        // Each owner gets the email of each request of their own business, and only those.
        await _api.DispatchOutboxAsync();
        var mineGot = _api.PlainEmailsTo(mine.Email);
        var theirsGot = _api.PlainEmailsTo(theirs.Email);
        Assert.Single(mineGot);
        Assert.StartsWith("Nueva solicitud de cita en ", mineGot[0].Subject);
        Assert.Contains("Corte de avisos", mineGot[0].Subject);
        Assert.Equal(2, theirsGot.Count);
        Assert.All(theirsGot, email => Assert.Contains("Masaje de avisos", email.Subject));
        Assert.DoesNotContain(theirsGot.Concat(mineGot), email => email.Body.Contains("551234000"));

        // The owner turns them off for themselves: the next request reaches the screens and no email goes out; the other
        // business keeps its own choice.
        var off = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(RequestEmails, new { enabled = false }));
        Assert.Equal((false, mine.Email), (off.GetProperty("enabled").GetBoolean(), off.GetProperty("email").GetString()));
        Assert.True((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());
        var unnoticed = await RequestAsync(stranger, mySlug, myService, "5512340004");
        await seenByMine.WaitForAsync(unnoticed.ToString());
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email));
    }

    [SqlServerFact]
    public async Task AppointmentHub_ShouldRefuse_ABusinessWithoutAppointments()
    {
        var restaurant = await _api.SignUpAsync("avisos-comida", TwoBusinessesApi.FoodBusiness);

        await using var refused = _api.Hub("/hubs/appointments", restaurant.AccessToken);
        await using var orders = _api.Hub("/hubs/orders", restaurant.AccessToken);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        refused.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        // The server aborts it as it connects: it never stays connected. Its orders hub is unaffected.
        try
        {
            await refused.StartAsync();
            await closed.Task.WaitAsync(Patience);
        }
        catch (Exception)
        {
            // Refused before the handshake ended.
        }

        Assert.Equal(HubConnectionState.Disconnected, refused.State);
        await orders.StartAsync();
        Assert.Equal(HubConnectionState.Connected, orders.State);
        Assert.Equal(HttpStatusCode.Forbidden, (await restaurant.Client.GetAsync(RequestEmails)).StatusCode);
    }

    [SqlServerFact]
    public async Task WhatsAppDeliveries_ShouldReachOnlyTheBusinessOfTheirNumber()
    {
        var seenByA = new Events();
        var seenByB = new Events();
        await using var chatA = await ConnectAsync("/hubs/chat", A.AccessToken, "NewMessage", seenByA, MessageBodyOf);
        await using var chatB = await ConnectAsync("/hubs/chat", B.AccessToken, "NewMessage", seenByB, MessageBodyOf);

        // Same order as the hub test: B's chat is live before A's message, and B's second message closes the window.
        var firstToB = $"Hola B {Guid.NewGuid():N}";
        await DeliverAsync(B.WhatsAppPhoneNumberId, firstToB);
        await seenByB.WaitForAsync(firstToB);
        var toA = $"Hola A {Guid.NewGuid():N}";
        await DeliverAsync(A.WhatsAppPhoneNumberId, toA);
        await seenByA.WaitForAsync(toA);
        var secondToB = $"Otra para B {Guid.NewGuid():N}";
        await DeliverAsync(B.WhatsAppPhoneNumberId, secondToB);
        await seenByB.WaitForAsync(secondToB);

        Assert.False(seenByB.Saw(toA));
        Assert.False(seenByA.Saw(firstToB));
        Assert.False(seenByA.Saw(secondToB));

        // The same customer wrote to both: each business has its own conversation, with only its messages.
        var chatsOfA = await ChatsAsync(A);
        var chatsOfB = await ChatsAsync(B);
        Assert.Contains(toA, chatsOfA);
        Assert.DoesNotContain(secondToB, chatsOfA);
        Assert.Contains(secondToB, chatsOfB);
        Assert.DoesNotContain(toA, chatsOfB);
    }

    [SqlServerFact]
    public async Task WhatsAppConversation_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var text = $"Pregunta para A {Guid.NewGuid():N}";
        await DeliverAsync(A.WhatsAppPhoneNumberId, text, fromPhone: "5215522223333");
        var before = await ConversationAsync(A, text);
        var conversation = before.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync($"/api/v1/whatsapp/chat/{conversation}/messages")).StatusCode);
        AssertRefused(await B.Client.PutAsync($"/api/v1/whatsapp/chat/{conversation}/read", null));
        AssertRefused(await B.Client.PutAsJsonAsync($"/api/v1/whatsapp/chat/{conversation}/auto-replies", new { paused = true }));
        Assert.DoesNotContain(conversation.ToString(), await ChatsAsync(B));

        var after = await ConversationAsync(A, text);
        Assert.Equal(before.GetProperty("unreadCount").GetInt32(), after.GetProperty("unreadCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("autoRepliesPausedUntil").ValueKind);
        var messages = await A.Client.GetFromJsonAsync<JsonElement>($"/api/v1/whatsapp/chat/{conversation}/messages");
        Assert.Equal(text, Assert.Single(messages.GetProperty("items").EnumerateArray()).GetProperty("body").GetString());
    }

    [SqlServerFact]
    public async Task WhatsAppNumber_OfAnotherBusiness_ShouldNotBeClaimed_AndShouldKeepReachingItsOwner()
    {
        var claim = await B.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/whatsapp", new
        {
            isEnabled = false,
            phoneNumberId = A.WhatsAppPhoneNumberId
        });

        Assert.Equal(HttpStatusCode.BadRequest, claim.StatusCode);
        Assert.Equal(
            UpdateWhatsAppConfigCommandHandler.PhoneNumberIdInUseError,
            (await claim.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
        var configOfB = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant/whatsapp");
        Assert.Equal(B.WhatsAppPhoneNumberId, configOfB.GetProperty("phoneNumberId").GetString());

        var text = $"Sigue siendo de A {Guid.NewGuid():N}";
        await DeliverAsync(A.WhatsAppPhoneNumberId, text, fromPhone: "5215533334444");
        Assert.Contains(text, await ChatsAsync(A));
        Assert.DoesNotContain(text, await ChatsAsync(B));
    }

    [SqlServerFact]
    public async Task WhatsAppDelivery_WithoutTheAppSignature_ShouldNotBeStored()
    {
        var text = $"Sin firma {Guid.NewGuid():N}";
        var body = TwoBusinessesApi.InboundMessage(A.WhatsAppPhoneNumberId, "5215544445555", text);
        await _api.ProcessWebhookInboxAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostWebhookAsync(body, signature: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostWebhookAsync(body, signature: "sha256=" + new string('0', 64))).StatusCode);
        var signedByAnotherApp = WhatsAppWebhookTestSupport.Sign(Encoding.UTF8.GetBytes(body), "secreto-de-otra-app");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostWebhookAsync(body, signedByAnotherApp)).StatusCode);

        Assert.Equal(0, await _api.ProcessWebhookInboxAsync());
        Assert.DoesNotContain(text, await ChatsAsync(A));
    }

    [SqlServerFact]
    public async Task WhatsAppDelivery_ForANumberOfNoBusiness_ShouldReachNone()
    {
        var text = $"Numero sin negocio {Guid.NewGuid():N}";

        await DeliverAsync("199999999999", text);

        Assert.DoesNotContain(text, await ChatsAsync(A));
        Assert.DoesNotContain(text, await ChatsAsync(B));
    }

    private async Task<HubConnection> ConnectAsync(
        string path, string accessToken, string eventName, Events events, Func<JsonElement, string> keyOf)
    {
        var connection = _api.Hub(path, accessToken);
        connection.On<JsonElement>(eventName, payload => events.Record(keyOf(payload)));
        await connection.StartAsync();
        return connection;
    }

    /// <summary>A signed delivery, stored by the webhook and processed as the inbox worker would.</summary>
    private async Task DeliverAsync(string phoneNumberId, string text, string fromPhone = "5215511112222")
    {
        var response = await _api.PostWebhookAsync(TwoBusinessesApi.InboundMessage(phoneNumberId, fromPhone, text));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(0, await _api.ProcessWebhookInboxAsync());
    }

    private static Task<string> ChatsAsync(Business business) =>
        business.Client.GetStringAsync("/api/v1/whatsapp/chat?pageSize=100");

    private static async Task<JsonElement> ConversationAsync(Business business, string lastMessage)
    {
        var page = await business.Client.GetFromJsonAsync<JsonElement>("/api/v1/whatsapp/chat?pageSize=100");
        return page.GetProperty("items").EnumerateArray()
            .Single(conversation => conversation.GetProperty("lastMessageBody").GetString() == lastMessage);
    }

    private static string OrderIdOf(JsonElement payload) => payload.GetProperty("orderId").GetString()!;

    private static string AppointmentIdOf(JsonElement payload) => payload.GetProperty("appointmentId").GetString()!;

    private const string RequestEmails = "/api/v1/appointments/request-emails";

    /// <summary>A customer's request online at <paramref name="slug"/>, at its first free time; its id.</summary>
    private static async Task<Guid> RequestAsync(HttpClient customer, string slug, Guid service, string phone)
    {
        var receipt = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments",
            CrossTenantOnlineBookingSqlServerTests.Request(service, await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, slug, service), phone)));
        var link = receipt.GetProperty("trackingToken").GetString()!;
        return (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = link })))
            .GetProperty("id").GetGuid();
    }

    private static string MessageBodyOf(JsonElement payload) => payload.GetProperty("message").GetProperty("body").GetString()!;

    private static void AssertRefused(HttpResponseMessage response)
    {
        // Not found, refused or invalid: never applied.
        Assert.False(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}");
        Assert.True((int)response.StatusCode is >= 400 and < 500, $"{(int)response.StatusCode}");
    }

    /// <summary>What one connection received, by key; waiting for a key returns once it arrives (or already did).</summary>
    private sealed class Events
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _received = new();

        public void Record(string key) => Slot(key).TrySetResult();

        public bool Saw(string key) => _received.TryGetValue(key, out var slot) && slot.Task.IsCompleted;

        public Task WaitForAsync(string key) => Slot(key).Task.WaitAsync(Patience);

        private TaskCompletionSource Slot(string key) =>
            _received.GetOrAdd(key, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
