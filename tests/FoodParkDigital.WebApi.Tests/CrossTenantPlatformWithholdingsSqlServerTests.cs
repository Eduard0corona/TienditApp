using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The CFDI de retenciones of the platforms (Fase 22J) through the API, on SQL Server: each business sees, registers and
/// removes only its own, the business header of an owner changes nothing, a CFDI of another business that says it
/// replaces one of mine takes nothing out, the same CFDI counts once in a business (and once in each), and the
/// withholdings are read with the reports and changed by owners and admins.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantPlatformWithholdingsSqlServerTests
{
    private const string Password = "Secreto1";
    private const string Uuid = "7874D580-DA5B-558F-B8EC-D92C57197242";

    private readonly TwoBusinessesApi _api;

    public CrossTenantPlatformWithholdingsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Withholdings_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("retiene-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("retiene-b", TwoBusinessesApi.FoodBusiness);
        var registered = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            "/api/v1/platform-withholdings", new { channel = OrderChannel.UberEats, xml = Xml(Uuid) }));
        var id = registered.GetProperty("id").GetGuid();

        // The other owner, even with the header of the business (only the platform admin may use it), finds nothing of mine.
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform-withholdings/{id}/remove")
               {
                   Content = JsonContent.Create(new { reason = "No es mío" })
               })
        {
            request.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(request)).StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform-withholdings?channel=UberEats&year=2026"))
        {
            request.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            var theirView = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(request));
            Assert.Equal(0, theirView.GetProperty("certificates").GetArrayLength());
            Assert.All(theirView.GetProperty("months").EnumerateArray(), month => Assert.Equal(0, month.GetProperty("certificateCount").GetInt32()));
        }

        // The same CFDI is another record in their business.
        var theirCopy = await theirs.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new { channel = OrderChannel.UberEats, xml = Xml(Uuid) });
        Assert.Equal(HttpStatusCode.OK, theirCopy.StatusCode);

        // Mine is as I left it, in my business only.
        var myView = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform-withholdings?channel=UberEats&year=2026");
        var certificate = myView.GetProperty("certificates").EnumerateArray().Single();
        Assert.Equal((id, JsonValueKind.Null), (certificate.GetProperty("id").GetGuid(), certificate.GetProperty("removedAt").ValueKind));
        Assert.Equal(1708.32m, myView.GetProperty("months")[8].GetProperty("certificateIsr").GetDecimal()
            + myView.GetProperty("months")[8].GetProperty("certificateVat").GetDecimal());
        Assert.Equal(
            new[] { mine.TenantId },
            await _api.QueryAsync(mine.TenantId, db => db.PlatformWithholdingCertificates.Select(c => c.TenantId).ToListAsync()));
        Assert.Equal(1, await _api.QueryAsync(theirs.TenantId, db => db.PlatformWithholdingCertificates.CountAsync()));
    }

    [SqlServerFact]
    public async Task ReplacementUploadedByAnotherBusiness_ShouldNotTakeMineOut()
    {
        var mine = await _api.SignUpAsync("retiene-sustituye-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("retiene-sustituye-b", TwoBusinessesApi.FoodBusiness);
        var registered = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            "/api/v1/platform-withholdings", new { channel = OrderChannel.UberEats, xml = Xml(Uuid) }));

        // Theirs says it replaces my folio: in their business there is nothing to take out, and mine still counts.
        var replacing = await theirs.Client.PostAsJsonAsync(
            "/api/v1/platform-withholdings",
            new { channel = OrderChannel.UberEats, xml = Xml("22222222-2222-4333-8444-555555555555", replaces: Uuid) });

        Assert.Equal(HttpStatusCode.OK, replacing.StatusCode);
        Assert.Equal(
            new[] { (registered.GetProperty("id").GetGuid(), (DateTimeOffset?)null) },
            (await _api.QueryAsync(mine.TenantId, db => db.PlatformWithholdingCertificates.Select(c => new { c.Id, c.RemovedAt }).ToListAsync()))
                .Select(c => (c.Id, c.RemovedAt)));
        Assert.Equal(0, await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(log => log.Action == "PlatformWithholdingCertificateRemoved")));
    }

    [SqlServerFact]
    public async Task Certificate_ShouldCountOnce_AndBeRemovedWithAReason()
    {
        var owner = await _api.SignUpAsync("retiene-ciclo", TwoBusinessesApi.FoodBusiness);
        var first = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(
            "/api/v1/platform-withholdings", new { channel = OrderChannel.Rappi, xml = Xml(Uuid) }));
        var id = first.GetProperty("id").GetGuid();

        var again = await owner.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new { channel = OrderChannel.DidiFood, xml = Xml(Uuid) });
        var duplicate = Problem(await again.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.Conflict, "Este CFDI ya está registrado en Rappi, septiembre de 2026."),
            (again.StatusCode, duplicate.Code, duplicate.Detail));

        var removed = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(
            $"/api/v1/platform-withholdings/{id}/remove", new { reason = "Era de Uber Eats" }));
        Assert.Equal("Era de Uber Eats", removed.GetProperty("removalReason").GetString());
        // Removed already, as when someone else did it after the list was loaded: 409, and the list is read again.
        var twice = await owner.Client.PostAsJsonAsync($"/api/v1/platform-withholdings/{id}/remove", new { reason = "Otra vez" });
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict), (twice.StatusCode, Problem(await twice.Content.ReadAsStringAsync()).Code));
        Assert.Equal(HttpStatusCode.OK,
            (await owner.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new { channel = OrderChannel.UberEats, xml = Xml(Uuid) })).StatusCode);
        Assert.Equal(
            new[] { "PlatformWithholdingCertificateRegistered", "PlatformWithholdingCertificateRegistered", "PlatformWithholdingCertificateRemoved" },
            (await _api.QueryAsync(owner.TenantId, db => db.AuditLogs
                .Where(log => log.EntityName == "PlatformWithholdingCertificate")
                .Select(log => log.Action)
                .ToListAsync())).Order());
    }

    [SqlServerFact]
    public async Task Withholdings_ShouldBeReadWithTheReports_AndChangedByOwnersAndAdmins()
    {
        var owner = await _api.SignUpAsync("retiene-equipo", TwoBusinessesApi.FoodBusiness);
        var registered = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(
            "/api/v1/platform-withholdings", new { channel = OrderChannel.UberEats, xml = Xml(Uuid) }));
        var id = registered.GetProperty("id").GetGuid();
        var reports = await EmployeeAsync(owner, appRole: 2, "ViewReports");
        var cashier = await EmployeeAsync(owner, appRole: 3, "ManageOrders");

        Assert.Equal(HttpStatusCode.OK, (await reports.GetAsync("/api/v1/platform-withholdings?channel=UberEats&year=2026")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/platform-withholdings?channel=UberEats&year=2026")).StatusCode);
        foreach (var staff in new[] { reports, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/platform-withholdings",
                new { channel = OrderChannel.Rappi, xml = Xml("11111111-2222-4333-8444-555555555555") })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/v1/platform-withholdings/{id}/remove",
                new { reason = "x" })).StatusCode);
        }
        Assert.Equal(1, await _api.QueryAsync(owner.TenantId, db => db.PlatformWithholdingCertificates.CountAsync(c => c.RemovedAt == null)));
    }

    [SqlServerFact]
    public async Task Certificates_ShouldBeRefused_WhenNotOfAPlatform_NotAVoucher_OrNotSaidWhole()
    {
        var owner = await _api.SignUpAsync("retiene-validas", TwoBusinessesApi.FoodBusiness);

        var counter = await owner.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new { channel = OrderChannel.Counter, xml = Xml(Uuid) });
        var withoutXml = await owner.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new { channel = OrderChannel.Rappi });
        var invoice = await owner.Client.PostAsJsonAsync("/api/v1/platform-withholdings", new
        {
            channel = OrderChannel.Rappi,
            xml = """<cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0"/>"""
        });
        var withoutYear = await owner.Client.GetAsync("/api/v1/platform-withholdings?channel=Rappi");

        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed), (counter.StatusCode, Problem(await counter.Content.ReadAsStringAsync()).Code));
        Assert.Equal(HttpStatusCode.BadRequest, withoutXml.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, PlatformWithholdingXml.InvoiceMessage),
            (invoice.StatusCode, Problem(await invoice.Content.ReadAsStringAsync()).Detail));
        Assert.Equal(HttpStatusCode.BadRequest, withoutYear.StatusCode);
        Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.PlatformWithholdingCertificates.CountAsync()));
    }

    /// <summary>
    /// A CFDI de retenciones 2.0 of September 2026: 2.5 % of ISR and half the IVA of $16,269.76; it replaces another when
    /// <paramref name="replaces"/> says which.
    /// </summary>
    private static string Xml(string uuid, string? replaces = null) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <retenciones:Retenciones xmlns:retenciones="http://www.sat.gob.mx/esquemas/retencionpago/2" Version="2.0" FechaExp="2026-10-02T22:28:50" CveRetenc="26" LugarExpRetenc="03810">
          {(replaces is null ? "" : $"<retenciones:CfdiRetenRelacionados TipoRelacion=\"04\" UUID=\"{replaces}\"/>")}
          <retenciones:Emisor RfcE="UBV121024TN8" NomDenRazSocE="UBER B.V." RegimenFiscalE="601"/>
          <retenciones:Receptor NacionalidadR="Nacional"><retenciones:Nacional RfcR="CLA150101AB1" NomDenRazSocR="COCINA LA ABUELA"/></retenciones:Receptor>
          <retenciones:Periodo MesIni="09" MesFin="09" Ejercicio="2026"/>
          <retenciones:Totales MontoTotOperacion="16269.76" MontoTotGrav="16269.76" MontoTotExent="0.00" MontoTotRet="1708.32">
            <retenciones:ImpRetenidos BaseRet="16269.76" ImpuestoRet="001" MontoRet="406.74" TipoPagoRet="03"/>
            <retenciones:ImpRetenidos BaseRet="2603.16" ImpuestoRet="002" MontoRet="1301.58" TipoPagoRet="01"/>
          </retenciones:Totales>
          <retenciones:Complemento>
            <tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" Version="1.1" UUID="{uuid}" FechaTimbrado="2026-10-02T22:28:50"/>
          </retenciones:Complemento>
        </retenciones:Retenciones>
        """;

    /// <summary>A member of the owner's team with the role and the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, int appRole, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    private static (string? Code, string? Detail) Problem(string body)
    {
        var problem = JsonDocument.Parse(body).RootElement;
        return (
            problem.TryGetProperty("code", out var code) ? code.GetString() : null,
            problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
