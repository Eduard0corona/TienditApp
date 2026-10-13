using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Appointments at the customer's address (Fase 22E) through the API: each business quotes and books visits only with its
/// own zones, sees only its own route, charges only its own appointments (once) and sets its own travel time; the activity
/// keeps the zone and the charge, never the address.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantHomeVisitsSqlServerTests
{
    private const string Appointments = "/api/v1/appointments";
    private const string HomeVisitSettings = "/api/v1/delivery-zones/home-visits";
    private const string Address = "Av. Juárez 123, Col. Centro";

    private readonly TwoBusinessesApi _api;

    public CrossTenantHomeVisitsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task VisitsOfAnotherBusiness_ShouldNotUseItsZones_ShowInItsRoute_NorBeChargedByIt()
    {
        var mine = await _api.SignUpAsync("visitas-a");
        var theirs = await _api.SignUpAsync("visitas-b");
        await ZoneAsync(mine.Client, "Centro", 80m, "72000");
        // Noon in Mexico City, the time zone of a new business: the same date there.
        var startAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(1).AddHours(18), TimeSpan.Zero);
        var date = DateOnly.FromDateTime(startAt.UtcDateTime);

        // The quote: my zone; the other business has none, and then none that reaches 72000.
        var quote = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Appointments}/visit-quote?postalCode=72000"));
        Assert.Equal(("Centro", 80m), (quote.GetProperty("zoneName").GetString(), quote.GetProperty("fee").GetDecimal()));
        Assert.Equal(HomeVisitRules.NoZonesMessage, await DetailAsync(await theirs.Client.GetAsync($"{Appointments}/visit-quote?postalCode=72000")));
        await ZoneAsync(theirs.Client, "Sur", 20m, "72999");
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"),
            await DetailAsync(await theirs.Client.GetAsync($"{Appointments}/visit-quote?postalCode=72000")));

        // Booking: with my zone; theirs does not reach 72000.
        var booked = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Appointments, Appointment(startAt, "72000")));
        var appointmentId = booked.GetProperty("id").GetGuid();
        Assert.Equal(80m, booked.GetProperty("visit").GetProperty("fee").GetDecimal());
        var refused = await theirs.Client.PostAsJsonAsync(Appointments, Appointment(startAt, "72000"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HomeVisitRules.NotCoveredMessage("72000"), await DetailAsync(refused));

        // The agenda filtered by where the appointments happen: mine at home; none at the business.
        var atHome = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Appointments}?homeVisit=true&pageSize=100"));
        var atTheBusiness = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Appointments}?homeVisit=false&pageSize=100"));
        Assert.Equal(appointmentId, Assert.Single(atHome.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Empty(atTheBusiness.GetProperty("items").EnumerateArray());

        // The route: mine has the visit; theirs is empty.
        var myRoute = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Appointments}/route?date={date:yyyy-MM-dd}"));
        var stop = Assert.Single(Assert.Single(myRoute.GetProperty("staff").EnumerateArray()).GetProperty("stops").EnumerateArray());
        Assert.Equal((appointmentId, Address), (stop.GetProperty("appointmentId").GetGuid(), stop.GetProperty("addressLine").GetString()));
        var theirRoute = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Appointments}/route?date={date:yyyy-MM-dd}"));
        Assert.Empty(theirRoute.GetProperty("staff").EnumerateArray());

        // The sale: the other business finds no such appointment; mine charges the visit once.
        var theirProduct = await ProductAsync(theirs.Client, "Corte ajeno");
        var notTheirs = await theirs.Client.PostAsJsonAsync("/api/v1/orders", Sale(theirProduct, appointmentId));
        Assert.Equal(CreateOrderCommandValidator.AppointmentNotFoundMessage, await DetailAsync(notTheirs));
        var myProduct = await ProductAsync(mine.Client, "Corte");
        var sale = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", Sale(myProduct, appointmentId)));
        Assert.Equal((95m, 80m), (sale.GetProperty("total").GetDecimal(), sale.GetProperty("visitCharge").GetProperty("fee").GetDecimal()));
        Assert.Equal(HomeVisitRules.AlreadySoldMessage,
            await DetailAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", Sale(myProduct, appointmentId))));
        var charged = await mine.Client.GetFromJsonAsync<JsonElement>($"{Appointments}/{appointmentId}");
        Assert.Equal((sale.GetProperty("id").GetGuid(), 3), (charged.GetProperty("saleOrderId").GetGuid(), charged.GetProperty("status").GetInt32()));

        Assert.Equal(
            new[] { (mine.TenantId, appointmentId) },
            (await _api.QueryAsync(mine.TenantId, db => db.OrderVisitCharges.ToListAsync())).Select(c => (c.TenantId, c.AppointmentId)));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.AppointmentVisits.ToListAsync()));
        var log = await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.SingleAsync(a => a.Action == "AppointmentVisitArranged"));
        Assert.DoesNotContain("Juárez", log.Metadata);
        Assert.DoesNotContain("72000", log.Metadata);
    }

    [SqlServerFact]
    public async Task TravelTime_ShouldBeEachBusinesssOwn()
    {
        var mine = await _api.SignUpAsync("traslado-a");
        var theirs = await _api.SignUpAsync("traslado-b");

        var set = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(HomeVisitSettings, new { travelMinutes = 45 }));
        var tooLong = await theirs.Client.PutAsJsonAsync(HomeVisitSettings, new { travelMinutes = 241 });

        Assert.Equal(45, set.GetProperty("travelMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(45, (await mine.Client.GetFromJsonAsync<JsonElement>(HomeVisitSettings)).GetProperty("travelMinutes").GetInt32());
        Assert.Equal(30, (await theirs.Client.GetFromJsonAsync<JsonElement>(HomeVisitSettings)).GetProperty("travelMinutes").GetInt32());
    }

    private static object Appointment(DateTimeOffset startAt, string postalCode) => new
    {
        customerName = "Ana",
        customerPhone = "5512345678",
        serviceName = "Corte a domicilio",
        // With someone who attends it, so the travel time is read on SQL Server too.
        staffName = "Luis",
        startAt,
        endAt = startAt.AddHours(1),
        visit = new { postalCode, addressLine = Address, references = "Portón negro" }
    };

    private static object Sale(Guid product, Guid appointmentId) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = false,
        customerName = "Ana",
        items = new[] { new { productId = product, quantity = 1, optionIds = Array.Empty<Guid>() } },
        payments = Array.Empty<object>(),
        appointmentId
    };

    private static async Task ZoneAsync(HttpClient owner, string name, decimal fee, params string[] postalCodes)
        => await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/delivery-zones", new
        {
            name,
            fee,
            minimumOrder = 0m,
            freeDeliveryFrom = (decimal?)null,
            postalCodes,
            isActive = true,
            revision = 0
        }));

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
}
