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
    public async Task AppointmentHubAndItsEmails_ShouldBeForWhoeverAnswersTheRequests_NotTheRestOfTheTeam()
    {
        var owner = await _api.SignUpAsync("avisos-equipo");
        var (slug, service) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(owner.Client, "Corte del equipo");
        var (cashier, cashierToken, cashierEmail, _) = await EmployeeAsync(owner, "ManageOrders");
        var (stylist, stylistToken, stylistEmail, stylistMember) = await EmployeeAsync(owner, "ManageAppointments");

        // Without the agenda: the hub refuses it and the choice is not theirs to make.
        await using var refused = _api.Hub("/hubs/appointments", cashierToken);
        await AssertRefusedAsync(refused);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(RequestEmails)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync(RequestEmails, new { enabled = true })).StatusCode);

        // With it: connected, and their own choice, off until they make it (only the owners get them by default).
        await using var accepted = _api.Hub("/hubs/appointments", stylistToken);
        await accepted.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(HubConnectionState.Connected, accepted.State);
        Assert.False((await TwoBusinessesApi.ReadAsync(await stylist.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await stylist.PutAsJsonAsync(RequestEmails, new { enabled = true }))).GetProperty("enabled").GetBoolean());
        // The owner's own stays as it was.
        Assert.True((await TwoBusinessesApi.ReadAsync(await owner.Client.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());

        // A request online, with the access the API reads for each one: the stylist who chose them and the owner get its
        // email; the cashier, who never chose them, does not.
        using var stranger = _api.AnonymousClient();
        await RequestAsync(stranger, slug, service, "5512340005");
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(stylistEmail));
        Assert.Single(_api.PlainEmailsTo(owner.Email));
        Assert.Empty(_api.PlainEmailsTo(cashierEmail));

        // The owner takes the agenda away from the stylist, who still chose them: her live connection closes (the access
        // sweep, every second here, checks the permission again), and the next request's email goes to the owner only,
        // because the stylist can no longer answer it.
        var stylistClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accepted.Closed += _ =>
        {
            stylistClosed.TrySetResult();
            return Task.CompletedTask;
        };
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync($"/api/v1/employees/{stylistMember}", new
        {
            tenantUserId = stylistMember,
            firstName = "Empleado",
            lastName = "De avisos",
            appRole = 3,
            tenantRole = 2,
            permissions = new[] { "ManageOrders" }
        }));
        await stylistClosed.Task.WaitAsync(Patience);
        Assert.Equal(HubConnectionState.Disconnected, accepted.State);
        await RequestAsync(stranger, slug, service, "5512340006");
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(stylistEmail));
        Assert.Equal(2, _api.PlainEmailsTo(owner.Email).Count);
    }

    [SqlServerFact]
    public async Task TheNoticesOfTheTeam_ShouldBeChosenByWhoeverRunsIt_InItsOwnBusinessOnly_ButNeverTheOwners()
    {
        // Fase 29Q (ADR 0071).
        var owner = await _api.SignUpAsync("avisos-por-equipo");
        var (slug, service) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(owner.Client, "Corte por equipo");
        var (receptionist, _, receptionistEmail, receptionistMember) = await EmployeeAsync(owner, "ManageAppointments");
        var team = await TwoBusinessesApi.ReadAsync(await owner.Client.GetAsync("/api/v1/employees?pageSize=50"));
        var ownerMember = team.GetProperty("items").EnumerateArray()
            .Single(member => member.GetProperty("tenantRole").GetString() == "Owner")
            .GetProperty("tenantUserId").GetGuid();

        // Someone of the team, or another business, does not choose them.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await receptionist.PutAsJsonAsync($"/api/v1/employees/{receptionistMember}", Edit(receptionistMember, requestEmails: true))).StatusCode);
        AssertRefused(await A.Client.PutAsJsonAsync($"/api/v1/employees/{receptionistMember}", Edit(receptionistMember, requestEmails: true)));
        Assert.False((await TwoBusinessesApi.ReadAsync(await receptionist.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());

        // The owner turns them on for the receptionist: she sees it in her profile, she is told who did it (Fase 30A, ADR 0081),
        // and the next request reaches her.
        var chosen = await TwoBusinessesApi.ReadAsync(
            await owner.Client.PutAsJsonAsync($"/api/v1/employees/{receptionistMember}", Edit(receptionistMember, requestEmails: true)));
        Assert.True(chosen.GetProperty("noticeEmails").GetProperty("requests").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await receptionist.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());
        await _api.DispatchOutboxAsync();
        var told = Assert.Single(_api.PlainEmailsTo(receptionistEmail));
        Assert.Equal("Dueño AVISOS-POR-EQUIPO cambió tus avisos por correo en Negocio AVISOS-POR-EQUIPO", told.Subject);
        using var stranger = _api.AnonymousClient();
        await RequestAsync(stranger, slug, service, "5512340031");
        await _api.DispatchOutboxAsync();
        Assert.Equal(2, _api.PlainEmailsTo(receptionistEmail).Count);
        Assert.StartsWith("Nueva solicitud de cita", _api.PlainEmailsTo(receptionistEmail)[^1].Subject);

        // The owner's own are chosen in their profile, not from the team.
        var ownersOwn = await owner.Client.PutAsJsonAsync($"/api/v1/employees/{ownerMember}", Edit(ownerMember, requestEmails: false));
        Assert.Equal(HttpStatusCode.BadRequest, ownersOwn.StatusCode);
        Assert.Contains("Mi perfil", await ownersOwn.Content.ReadAsStringAsync());
        Assert.True((await TwoBusinessesApi.ReadAsync(await owner.Client.GetAsync(RequestEmails))).GetProperty("enabled").GetBoolean());
    }

    /// <summary>The edit of a member of the team, with the agenda, and the notices to choose.</summary>
    private static object Edit(Guid member, bool? requestEmails) => new
    {
        tenantUserId = member,
        firstName = "Empleado",
        lastName = "De avisos",
        appRole = 3,
        tenantRole = 2,
        permissions = new[] { "ManageAppointments" },
        requestEmails
    };

    [SqlServerFact]
    public async Task CancellationsByTheCustomer_ShouldReachOnlyTheirBusiness_OnItsScreensAndByEmailToWhoGetsThem()
    {
        // Fase 29O (ADR 0069).
        var mine = await _api.SignUpAsync("cancela-a");
        var theirs = await _api.SignUpAsync("cancela-b");
        var (mySlug, myService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte que se cancela");
        var (theirSlug, theirService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(theirs.Client, "Masaje que se cancela");
        using var stranger = _api.AnonymousClient();
        var ofMine = await RequestWithLinkAsync(stranger, mySlug, myService, "5512340011");
        var firstOfTheirs = await RequestWithLinkAsync(stranger, theirSlug, theirService, "5512340012");
        var secondOfTheirs = await RequestWithLinkAsync(stranger, theirSlug, theirService, "5512340013");
        var seenByMine = new Events();
        var seenByTheirs = new Events();
        var payloadsOfMine = new ConcurrentQueue<JsonElement>();
        await using var hubMine = await ConnectAsync("/hubs/appointments", mine.AccessToken, "AppointmentCancelled", seenByMine, payload =>
        {
            payloadsOfMine.Enqueue(payload.Clone());
            return AppointmentIdOf(payload);
        });
        await using var hubTheirs = await ConnectAsync("/hubs/appointments", theirs.AccessToken, "AppointmentCancelled", seenByTheirs, AppointmentIdOf);

        // Theirs is live before mine is cancelled; then theirs gets another: had mine been sent to theirs, it would have
        // arrived before that one.
        await CancelFromLinkAsync(stranger, firstOfTheirs.Token);
        await seenByTheirs.WaitForAsync(firstOfTheirs.Id.ToString());
        await CancelFromLinkAsync(stranger, ofMine.Token);
        await seenByMine.WaitForAsync(ofMine.Id.ToString());
        await CancelFromLinkAsync(stranger, secondOfTheirs.Token);
        await seenByTheirs.WaitForAsync(secondOfTheirs.Id.ToString());
        Assert.False(seenByTheirs.Saw(ofMine.Id.ToString()));
        Assert.False(seenByMine.Saw(firstOfTheirs.Id.ToString()));
        Assert.False(seenByMine.Saw(secondOfTheirs.Id.ToString()));
        // What it was for and when; never who cancelled it.
        var payload = Assert.Single(payloadsOfMine);
        Assert.Equal(
            ["appointmentId", "fromTeam", "homeVisit", "serviceName", "startAt", "wasRequest"],
            payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(("Corte que se cancela", true, false), (payload.GetProperty("serviceName").GetString(),
            payload.GetProperty("wasRequest").GetBoolean(), payload.GetProperty("fromTeam").GetBoolean()));

        // Each owner gets the email of each cancellation of their own business, and only those.
        await _api.DispatchOutboxAsync();
        var mineGot = _api.PlainEmailsTo(mine.Email).Where(e => e.Subject.Contains("cancelada")).ToList();
        var theirsGot = _api.PlainEmailsTo(theirs.Email).Where(e => e.Subject.Contains("cancelada")).ToList();
        Assert.Single(mineGot);
        Assert.StartsWith("Solicitud de cita cancelada en ", mineGot[0].Subject);
        Assert.Contains("Corte que se cancela", mineGot[0].Subject);
        Assert.Equal(2, theirsGot.Count);
        Assert.All(theirsGot, email => Assert.Contains("Masaje que se cancela", email.Subject));
        Assert.DoesNotContain(theirsGot.Concat(mineGot), email => email.Body.Contains("551234001"));

        // The owner turns the agenda's notices off for themselves: the next cancellation reaches the screens and no email
        // goes out.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(RequestEmails, new { enabled = false }));
        var unnoticed = await RequestWithLinkAsync(stranger, mySlug, myService, "5512340014");
        await CancelFromLinkAsync(stranger, unnoticed.Token);
        await seenByMine.WaitForAsync(unnoticed.Id.ToString());
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email), e => e.Subject.Contains("cancelada"));
    }

    [SqlServerFact]
    public async Task CancelledOnlineOrders_ShouldReachByEmailOnlyTheirKitchen_ForWhoeverConfirmsOrdersAndChoseThem()
    {
        // Fase 30B (ADR 0082).
        var mine = await _api.SignUpAsync("cancela-aviso-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("cancela-aviso-b", TwoBusinessesApi.FoodBusiness);
        var (cashier, _, cashierEmail, _) = await EmployeeAsync(mine, "ManageOrders");
        var (clerk, _, clerkEmail, _) = await EmployeeAsync(mine, "ManageCatalog");

        // The choice is for whoever confirms the orders: off for a member of the team until they choose, on for the owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync(OrderCancellationEmails)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PutAsJsonAsync(OrderCancellationEmails, new { enabled = true })).StatusCode);
        Assert.False((await TwoBusinessesApi.ReadAsync(await cashier.GetAsync(OrderCancellationEmails))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await cashier.PutAsJsonAsync(OrderCancellationEmails, new { enabled = true }))).GetProperty("enabled").GetBoolean());
        var owners = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(OrderCancellationEmails));
        Assert.Equal((true, mine.Email), (owners.GetProperty("enabled").GetBoolean(), owners.GetProperty("email").GetString()));

        // A customer of each kitchen cancels from the link of the receipt: each hears only of its own.
        using var stranger = _api.AnonymousClient();
        var myKitchen = await KitchenAsync(mine);
        var theirKitchen = await KitchenAsync(theirs);
        await CancelledOrderAsync(stranger, myKitchen, "5522107765");
        await CancelledOrderAsync(stranger, theirKitchen, "5522107766");
        await _api.DispatchOutboxAsync();

        var ownerGot = Assert.Single(_api.PlainEmailsTo(mine.Email), e => e.Subject.StartsWith("Pedido cancelado"));
        Assert.StartsWith("Pedido cancelado en Negocio CANCELA-AVISO-A: ORD-", ownerGot.Subject);
        Assert.Contains("antes de que lo aceptaran", ownerGot.Body);
        Assert.DoesNotContain("5522107765", ownerGot.Body + ownerGot.Subject);
        Assert.DoesNotContain("Tacos", ownerGot.Body);
        Assert.Single(_api.PlainEmailsTo(cashierEmail), e => e.Subject.StartsWith("Pedido cancelado en Negocio CANCELA-AVISO-A"));
        Assert.Empty(_api.PlainEmailsTo(clerkEmail));
        Assert.StartsWith("Pedido cancelado en Negocio CANCELA-AVISO-B",
            Assert.Single(_api.PlainEmailsTo(theirs.Email), e => e.Subject.StartsWith("Pedido cancelado")).Subject);

        // The owner turns them off for themselves; the cashier keeps theirs, and the other kitchen its own.
        Assert.False((await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(OrderCancellationEmails, new { enabled = false }))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(OrderCancellationEmails))).GetProperty("enabled").GetBoolean());
        await CancelledOrderAsync(stranger, myKitchen, "5522107767");
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email), e => e.Subject.StartsWith("Pedido cancelado"));
        Assert.Equal(2, _api.PlainEmailsTo(cashierEmail).Count(e => e.Subject.StartsWith("Pedido cancelado")));
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

    /// <summary>An order of the online menu that its customer cancels from the link of its receipt, before it is accepted.</summary>
    private static async Task CancelledOrderAsync(HttpClient customer, (SignedUp Owner, string Slug, Guid Product) kitchen, string phone)
    {
        var token = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{kitchen.Slug}/orders", new
        {
            customerName = "Valeria Ruiz",
            customerPhone = phone,
            items = new[] { new { productId = kitchen.Product, quantity = 1 } }
        }))).GetProperty("trackingToken").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await customer.PostAsJsonAsync("/api/v1/public/orders/tracking/cancel", new { token })).StatusCode);
    }

    [SqlServerFact]
    public async Task AnswersToQuotes_ShouldReachByEmailOnlyTheirBusiness_ForWhoeverWorksTheQuotesAndChoseThem()
    {
        // Fase 29O (ADR 0069).
        var mine = await _api.SignUpAsync("responde-aviso-a");
        var theirs = await _api.SignUpAsync("responde-aviso-b");
        var (cashier, _, cashierEmail, _) = await EmployeeAsync(mine, "ManageOrders");
        var (stylist, _, stylistEmail, _) = await EmployeeAsync(mine, "ManageAppointments");

        // The choice is for whoever works the quotes: off for a member of the team until they choose, on for the owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await stylist.GetAsync(QuoteReplyEmails)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stylist.PutAsJsonAsync(QuoteReplyEmails, new { enabled = true })).StatusCode);
        Assert.False((await TwoBusinessesApi.ReadAsync(await cashier.GetAsync(QuoteReplyEmails))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await cashier.PutAsJsonAsync(QuoteReplyEmails, new { enabled = true }))).GetProperty("enabled").GetBoolean());
        var owners = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(QuoteReplyEmails));
        Assert.Equal((true, mine.Email), (owners.GetProperty("enabled").GetBoolean(), owners.GetProperty("email").GetString()));

        // A customer of each answers from the link: each business hears only of its own.
        var (_, myToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(mine.Client);
        var (_, theirToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(theirs.Client);
        using var stranger = _api.AnonymousClient();
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply",
            new { token = myToken, accepted = true, response = "Los espero el jueves, mi número es 5522107765" }));
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = theirToken, accepted = false }));
        await _api.DispatchOutboxAsync();

        var ownerGot = Assert.Single(_api.PlainEmailsTo(mine.Email), e => e.Subject.StartsWith("Cotización"));
        Assert.StartsWith("Cotización aceptada en ", ownerGot.Subject);
        Assert.Contains("aceptó desde su enlace la cotización <strong>COT-", ownerGot.Body);
        Assert.DoesNotContain("jueves", ownerGot.Body);
        Assert.DoesNotContain("5522107765", ownerGot.Body);
        Assert.Single(_api.PlainEmailsTo(cashierEmail), e => e.Subject.StartsWith("Cotización aceptada en "));
        Assert.Empty(_api.PlainEmailsTo(stylistEmail));
        Assert.StartsWith("Cotización rechazada en ", Assert.Single(_api.PlainEmailsTo(theirs.Email), e => e.Subject.StartsWith("Cotización")).Subject);

        // The owner turns them off for themselves; the cashier keeps theirs, and the other business its own.
        Assert.False((await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(QuoteReplyEmails, new { enabled = false }))).GetProperty("enabled").GetBoolean());
        Assert.True((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(QuoteReplyEmails))).GetProperty("enabled").GetBoolean());
        var (_, nextToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(mine.Client);
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = nextToken, accepted = false }));
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.PlainEmailsTo(mine.Email), e => e.Subject.StartsWith("Cotización"));
        Assert.Equal(2, _api.PlainEmailsTo(cashierEmail).Count(e => e.Subject.StartsWith("Cotización")));
    }

    [SqlServerFact]
    public async Task AnswersToQuotes_ShouldReachTheScreensOfOnlyTheirBusiness_ForWhoeverWorksTheQuotes()
    {
        // Fase 29R (ADR 0072).
        var mine = await _api.SignUpAsync("responde-vivo-a");
        var theirs = await _api.SignUpAsync("responde-vivo-b");
        var restaurant = await _api.SignUpAsync("responde-vivo-comida", TwoBusinessesApi.FoodBusiness);
        var (_, cashierToken, _, _) = await EmployeeAsync(mine, "ManageOrders");
        var (_, stylistToken, _, _) = await EmployeeAsync(mine, "ManageAppointments");

        // Whoever does not work the quotes, or a business without them, never stays connected.
        await using (var stylists = _api.Hub("/hubs/quotes", stylistToken))
            await AssertRefusedAsync(stylists);
        await using (var restaurants = _api.Hub("/hubs/quotes", restaurant.AccessToken))
            await AssertRefusedAsync(restaurants);

        var seenByMine = new Events();
        var seenByCashier = new Events();
        var seenByTheirs = new Events();
        await using var hubMine = await ConnectAsync("/hubs/quotes", mine.AccessToken, "QuoteAnswered", seenByMine, QuoteAnswerOf);
        await using var hubCashier = await ConnectAsync("/hubs/quotes", cashierToken, "QuoteAnswered", seenByCashier, QuoteAnswerOf);
        await using var hubTheirs = await ConnectAsync("/hubs/quotes", theirs.AccessToken, "QuoteAnswered", seenByTheirs, QuoteAnswerOf);
        var (myQuote, myToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(mine.Client);
        var (theirQuote, theirToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(theirs.Client);
        var (secondOfTheirs, secondToken) = await CrossTenantQuoteSqlServerTests.SentQuoteAsync(theirs.Client);
        using var stranger = _api.AnonymousClient();

        // Theirs is live (its own answer reaches it) before mine gets one; then theirs gets another: had mine been sent
        // to theirs, it would have arrived before that one.
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = theirToken, accepted = false }));
        await seenByTheirs.WaitForAsync($"{theirQuote}:False");
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply",
            new { token = myToken, accepted = true, response = "Los espero el jueves" }));
        await seenByMine.WaitForAsync($"{myQuote}:True");
        await seenByCashier.WaitForAsync($"{myQuote}:True");
        await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/quotes/reply", new { token = secondToken, accepted = true }));
        await seenByTheirs.WaitForAsync($"{secondOfTheirs}:True");

        Assert.False(seenByTheirs.Saw($"{myQuote}:True"));
        Assert.False(seenByMine.Saw($"{theirQuote}:False"));
        Assert.False(seenByMine.Saw($"{secondOfTheirs}:True"));
        Assert.False(seenByCashier.Saw($"{secondOfTheirs}:True"));
    }

    [SqlServerFact]
    public async Task ARefusedConnection_ShouldBeClosedForGood_NotReconnectedBySignalR()
    {
        // The server aborts it once connected (Context.Abort): its close forbids reconnecting, so a client that reconnects by
        // itself, as the app's does, gets Closed and never Reconnecting. The app's client backs off its own retries from there.
        var restaurant = await _api.SignUpAsync("avisos-sin-reconexion", TwoBusinessesApi.FoodBusiness);
        await using var refused = _api.Hub("/hubs/appointments", restaurant.AccessToken, reconnect: true);
        var reconnecting = 0;
        refused.Reconnecting += _ =>
        {
            Interlocked.Increment(ref reconnecting);
            return Task.CompletedTask;
        };

        var handshakePassed = await AssertRefusedAsync(refused);
        await Task.Delay(TimeSpan.FromSeconds(1));

        // The handshake passed before OnConnectedAsync refused it: the case where a reconnect would have looped.
        Assert.True(handshakePassed);
        Assert.Equal(0, reconnecting);
        Assert.Equal(HubConnectionState.Disconnected, refused.State);
    }

    [SqlServerFact]
    public async Task AppointmentHub_ShouldRefuse_ABusinessWithoutAppointments()
    {
        var restaurant = await _api.SignUpAsync("avisos-comida", TwoBusinessesApi.FoodBusiness);

        await using var refused = _api.Hub("/hubs/appointments", restaurant.AccessToken);
        await using var orders = _api.Hub("/hubs/orders", restaurant.AccessToken);

        // The server aborts it as it connects: it never stays connected. Its orders hub is unaffected.
        await AssertRefusedAsync(refused);
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

    /// <summary>Which quote was answered and how, and that the notice says its folio and version (never what the customer wrote).</summary>
    private static string QuoteAnswerOf(JsonElement payload)
    {
        Assert.StartsWith("COT-", payload.GetProperty("number").GetString());
        Assert.Equal(1, payload.GetProperty("revision").GetInt32());
        Assert.False(payload.TryGetProperty("response", out _));
        return $"{payload.GetProperty("quoteId").GetString()}:{payload.GetProperty("accepted").GetBoolean()}";
    }

    /// <summary>
    /// A connection the server refuses: either the start fails, or the server closes it right after the handshake (it aborts
    /// it in OnConnectedAsync). Either way it ends disconnected and stays so.
    /// </summary>
    /// <returns>The handshake passed (the server closed it afterwards).</returns>
    private static async Task<bool> AssertRefusedAsync(HubConnection connection)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var started = true;
        try
        {
            await connection.StartAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or System.Net.WebSockets.WebSocketException)
        {
            started = false;
        }

        if (started)
            await closed.Task.WaitAsync(Patience);
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
        return started;
    }

    /// <summary>
    /// A member of the owner's team with the permissions given, signed in with its own password: its client, token, email
    /// and membership.
    /// </summary>
    private async Task<(HttpClient Client, string AccessToken, string Email, Guid Member)> EmployeeAsync(SignedUp owner, params string[] permissions)
    {
        var email = $"avisos-{Guid.NewGuid():N}@demo.com";
        var created = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = EmployeePassword,
            firstName = "Empleado",
            lastName = "De avisos",
            appRole = 3,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = EmployeePassword }));
        var token = signedIn.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return (client, token, email, created.GetProperty("tenantUserId").GetGuid());
    }

    private const string EmployeePassword = "Secreto1";

    private const string RequestEmails = "/api/v1/appointments/request-emails";

    private const string QuoteReplyEmails = "/api/v1/quotes/reply-emails";

    private const string OrderCancellationEmails = "/api/v1/orders/cancellation-emails";

    /// <summary>A customer's request online at <paramref name="slug"/>, at its first free time; its id.</summary>
    private static async Task<Guid> RequestAsync(HttpClient customer, string slug, Guid service, string phone)
        => (await RequestWithLinkAsync(customer, slug, service, phone)).Id;

    /// <summary>A customer's request online at <paramref name="slug"/>, at its first free time; its id and its link.</summary>
    private static async Task<(Guid Id, string Token)> RequestWithLinkAsync(HttpClient customer, string slug, Guid service, string phone)
    {
        var receipt = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments",
            CrossTenantOnlineBookingSqlServerTests.Request(service, await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, slug, service), phone)));
        var link = receipt.GetProperty("trackingToken").GetString()!;
        var id = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = link })))
            .GetProperty("id").GetGuid();
        return (id, link);
    }

    /// <summary>Its customer cancels the appointment from its link, without an account.</summary>
    private static async Task CancelFromLinkAsync(HttpClient customer, string token)
        => await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"{CrossTenantOnlineBookingSqlServerTests.Tracking}/cancel", new { token }));

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
