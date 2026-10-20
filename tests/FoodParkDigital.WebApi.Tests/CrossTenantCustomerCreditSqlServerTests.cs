using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Fiado (Fase 26A) through the API: each business gives credit only to its own customers, sells on credit, lists who owes
/// and takes account payments only over its own orders, even for the same phone; the owner sets the limit, and selling on
/// credit or taking a payment needs ChargeOrders. Fase 26B: each business forgives only what its own customers owe it and
/// keeps its own term, and only owners and admins forgive or set the term. Fase 26C: each business gives terms only to its
/// own customers and keeps its own credit reminders, and only owners and admins set either.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantCustomerCreditSqlServerTests
{
    private const string Customers = "/api/v1/customers";
    private const string Receivables = "/api/v1/customers/receivables";
    private const string Account = "/api/v1/customers/account";
    private const string Password = "Secreto1";
    private const string Phone = "5512345678";

    private readonly TwoBusinessesApi _api;

    public CrossTenantCustomerCreditSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Credit_ShouldBeOnlyTheBusinesses_EvenForTheSamePhone()
    {
        var mine = await _api.SignUpAsync("fiado-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("fiado-b", TwoBusinessesApi.FoodBusiness);
        var myProduct = await ProductAsync(mine.Client, "Pan dulce");
        var theirProduct = await ProductAsync(theirs.Client, "Bolillo");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        // The same person is a customer of both businesses.
        var myCustomer = await TwoBusinessesApi.IdAsync(mine.Client, Customers, new { name = "Ana López", phone = Phone });
        var theirCustomer = await TwoBusinessesApi.IdAsync(theirs.Client, Customers, new { name = "Ana L.", phone = Phone });

        // Only each business gives its customer credit.
        Assert.Equal(HttpStatusCode.NotFound,
            (await theirs.Client.PutAsJsonAsync($"{Customers}/{myCustomer}/credit-limit", new { creditLimit = 500m })).StatusCode);
        var limited = await TwoBusinessesApi.ReadAsync(
            await mine.Client.PutAsJsonAsync($"{Customers}/{myCustomer}/credit-limit", new { creditLimit = 500m }));
        Assert.Equal(500m, limited.GetProperty("creditLimit").GetDecimal());
        Assert.Null(await _api.QueryAsync(theirs.TenantId, db => db.Customers.Where(c => c.Id == theirCustomer).Select(c => c.CreditLimit).SingleAsync()));

        // Another business cannot sell on credit to my customer: it is not one of its customers.
        var withMyCustomer = await theirs.Client.PostAsJsonAsync("/api/v1/orders", CreditSale(theirProduct, myCustomer));
        Assert.Equal(HttpStatusCode.BadRequest, withMyCustomer.StatusCode);
        var withTheirs = await theirs.Client.PostAsJsonAsync("/api/v1/orders", CreditSale(theirProduct, theirCustomer));
        Assert.Equal((HttpStatusCode.BadRequest, CustomerCredit.NoCreditMessage("Ana L.")),
            (withTheirs.StatusCode, await DetailAsync(withTheirs)));

        var sale = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", CreditSale(myProduct, myCustomer)));
        Assert.True(sale.GetProperty("soldOnCredit").GetBoolean());
        var owed = sale.GetProperty("balance").GetDecimal();

        // Who owes: mine lists Ana; theirs lists nobody, and her account there owes nothing, by customer or by phone.
        var myReceivables = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Receivables));
        Assert.Equal(owed, myReceivables.GetProperty("totalOwed").GetDecimal());
        Assert.Equal(myCustomer, Assert.Single(myReceivables.GetProperty("page").GetProperty("items").EnumerateArray()).GetProperty("customerId").GetGuid());
        var theirReceivables = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Receivables));
        Assert.Equal(0m, theirReceivables.GetProperty("totalOwed").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.GetAsync($"{Account}?customerId={myCustomer}")).StatusCode);
        var theirAccount = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Account}?phone={Phone}"));
        Assert.Equal((0m, 0), (theirAccount.GetProperty("owed").GetDecimal(), theirAccount.GetProperty("orders").GetArrayLength()));

        // A payment in another business does not touch my orders.
        var theirPayment = await theirs.Client.PostAsJsonAsync($"{Account}/payments", new { phone = Phone, amount = owed, method = 0 });
        Assert.Equal((HttpStatusCode.BadRequest, PayCustomerAccountCommandHandler.NothingOwedMessage),
            (theirPayment.StatusCode, await DetailAsync(theirPayment)));
        Assert.Equal(0m, await _api.QueryAsync(mine.TenantId, db => db.Orders.SumAsync(o => o.PaidAmount)));

        var paid = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"{Account}/payments",
            new { customerId = myCustomer, amount = owed, method = 0, cashReceived = owed + 10m }));
        Assert.Equal(0m, paid.GetProperty("owed").GetDecimal());
        Assert.Equal(["CustomerCreditLimitUpdated", "CustomerAccountPaid"],
            await _api.QueryAsync(mine.TenantId, db => db.AuditLogs
                .Where(l => l.Action == "CustomerCreditLimitUpdated" || l.Action == "CustomerAccountPaid")
                .OrderBy(l => l.Timestamp)
                .Select(l => l.Action)
                .ToListAsync()));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs
            .CountAsync(l => l.Action == "CustomerCreditLimitUpdated" || l.Action == "CustomerAccountPaid")));
    }

    [SqlServerFact]
    public async Task Credit_ShouldNeed_TheOwnerForTheLimit_AndChargeOrdersToSellOrCollect()
    {
        var owner = await _api.SignUpAsync("fiado-equipo", TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(owner.Client, "Concha");
        var customer = await TwoBusinessesApi.IdAsync(owner.Client, Customers, new { name = "Ana López", phone = Phone });
        var looksAfterCustomers = await EmployeeAsync(owner, "ManageCustomers");
        var takesOrders = await EmployeeAsync(owner, "ManageOrders");
        var cashier = await EmployeeAsync(owner, "ManageOrders", "ChargeOrders", "ManageCash");

        // Only owners and admins give credit.
        foreach (var staff in new[] { looksAfterCustomers, takesOrders, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await staff.PutAsJsonAsync($"{Customers}/{customer}/credit-limit", new { creditLimit = 500m })).StatusCode);
        }
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync($"{Customers}/{customer}/credit-limit", new { creditLimit = 500m }));

        // Selling on credit is charging: taking orders is not enough.
        Assert.Equal(HttpStatusCode.Forbidden, (await takesOrders.PostAsJsonAsync("/api/v1/orders", CreditSale(product, customer))).StatusCode);
        await TwoBusinessesApi.IdAsync(cashier, "/api/v1/cash/open", new { openingAmount = 0m });
        var sale = await TwoBusinessesApi.ReadAsync(await cashier.PostAsJsonAsync("/api/v1/orders", CreditSale(product, customer)));
        var owed = sale.GetProperty("balance").GetDecimal();

        // Who looks after customers and who charges read who owes; who only takes orders does not.
        Assert.Equal(HttpStatusCode.OK, (await looksAfterCustomers.GetAsync(Receivables)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync($"{Account}?customerId={customer}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await takesOrders.GetAsync(Receivables)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await takesOrders.GetAsync($"{Account}?customerId={customer}")).StatusCode);

        // Only who charges takes a payment, into their own drawer.
        var payment = new { customerId = customer, amount = owed, method = 1 };
        Assert.Equal(HttpStatusCode.Forbidden, (await looksAfterCustomers.PostAsJsonAsync($"{Account}/payments", payment)).StatusCode);
        Assert.Equal(0m, (await TwoBusinessesApi.ReadAsync(await cashier.PostAsJsonAsync($"{Account}/payments", payment))).GetProperty("owed").GetDecimal());
    }

    [SqlServerFact]
    public async Task WriteOffsAndTerm_ShouldBeOnlyTheBusinesses_AndTheOwners()
    {
        var mine = await _api.SignUpAsync("condonar-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("condonar-b", TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(mine.Client, "Pan dulce");
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, Customers, new { name = "Ana López", phone = Phone });
        await TwoBusinessesApi.IdAsync(theirs.Client, Customers, new { name = "Ana L.", phone = Phone });
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Customers}/{customer}/credit-limit", new { creditLimit = 500m }));
        var sale = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", CreditSale(product, customer)));
        var orderId = sale.GetProperty("id").GetGuid();
        var owed = sale.GetProperty("balance").GetDecimal();
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash", "ManageCustomers");

        // Another business, for the same phone or naming my order, forgives nothing of mine: its Ana owes it nothing.
        var byPhone = await theirs.Client.PostAsJsonAsync($"{Account}/write-offs", new { phone = Phone, amount = 10m, reason = 3, expectedOwed = owed });
        var byOrder = await theirs.Client.PostAsJsonAsync($"{Account}/write-offs", new { phone = Phone, orderId, amount = 10m, reason = 3, expectedOwed = owed });
        Assert.Equal((HttpStatusCode.BadRequest, WriteOffCustomerAccountCommandHandler.NothingOwedMessage), (byPhone.StatusCode, await DetailAsync(byPhone)));
        Assert.Equal(HttpStatusCode.BadRequest, byOrder.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await theirs.Client.PostAsJsonAsync($"{Account}/write-offs", new { customerId = customer, amount = 10m, reason = 3, expectedOwed = owed })).StatusCode);

        // Its term is its own.
        var theirTerm = await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync($"{Customers}/credit-term", new { creditTermDays = 30 }));
        Assert.Equal(30, theirTerm.GetProperty("creditTermDays").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Receivables))).GetProperty("creditTermDays").ValueKind);

        // Who charges and looks after customers neither forgives nor sets the term: owners and admins do.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostAsJsonAsync($"{Account}/write-offs", new { customerId = customer, amount = 10m, reason = 3, expectedOwed = owed })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"{Customers}/credit-term", new { creditTermDays = 7 })).StatusCode);

        var forgiven = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"{Account}/write-offs",
            new { customerId = customer, orderId, amount = 10m, reason = 99, notes = "Pan quemado", expectedOwed = owed }));
        Assert.Equal(owed - 10m, forgiven.GetProperty("owed").GetDecimal());
        var detail = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/orders/{orderId}"));
        Assert.Equal((10m, owed - 10m), (detail.GetProperty("writtenOffAmount").GetDecimal(), detail.GetProperty("balance").GetDecimal()));
        Assert.Equal("Pan quemado", Assert.Single(detail.GetProperty("writeOffs").EnumerateArray()).GetProperty("notes").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.OrderWriteOffs.CountAsync()));
        Assert.Equal((1, 0), (
            await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CustomerAccountWrittenOff")),
            await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CustomerAccountWrittenOff"))));

        // Undoing it is mine too: another business does not find it, and who charges cannot undo it.
        var writeOffId = forgiven.GetProperty("recentWriteOffs")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"{Account}/write-offs/{writeOffId}/reversal", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync($"{Account}/write-offs/{writeOffId}/reversal", null)).StatusCode);
        var undone = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"{Account}/write-offs/{writeOffId}/reversal", null));
        Assert.Equal(owed, undone.GetProperty("owed").GetDecimal());
        Assert.Equal(JsonValueKind.String, undone.GetProperty("recentWriteOffs")[0].GetProperty("reversedAt").ValueKind);
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CustomerAccountWriteOffReversed")));
    }

    [SqlServerFact]
    public async Task CustomerTermsAndReminders_ShouldBeOnlyTheBusinesses_AndTheOwners()
    {
        var mine = await _api.SignUpAsync("recordar-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("recordar-b", TwoBusinessesApi.FoodBusiness);
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, Customers, new { name = "Ana López", phone = Phone });
        var theirCustomer = await TwoBusinessesApi.IdAsync(theirs.Client, Customers, new { name = "Ana L.", phone = Phone });
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash", "ManageCustomers", "ManageCampaigns");

        // Another business does not find my customer to give her a term; its own Ana keeps its own.
        Assert.Equal(HttpStatusCode.NotFound,
            (await theirs.Client.PutAsJsonAsync($"{Customers}/{customer}/credit-term", new { creditTermDays = 3 })).StatusCode);
        var theirTerm = await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync($"{Customers}/{theirCustomer}/credit-term", new { creditTermDays = 3 }));
        Assert.Equal(3, theirTerm.GetProperty("creditTermDays").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Customers}/{customer}"))).GetProperty("creditTermDays").ValueKind);

        // Who charges and looks after customers (and runs campaigns) neither gives a term nor sets the reminders.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PutAsJsonAsync($"{Customers}/{customer}/credit-term", new { creditTermDays = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"{Customers}/credit-reminders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PutAsJsonAsync($"{Customers}/credit-reminders", new { enabled = false, everyDays = 7 })).StatusCode);
        var mineTerm = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Customers}/{customer}/credit-term", new { creditTermDays = 7 }));
        Assert.Equal(7, mineTerm.GetProperty("creditTermDays").GetInt32());

        // What the reminders say is the owner's too: campaigns may write their own templates, not the fiado's.
        const string templates = "/api/v1/messaging/templates";
        Assert.Equal(HttpStatusCode.OK,
            (await cashier.PostAsJsonAsync(templates, new { name = "Promo", purpose = 0, body = "Hola {nombre}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.PostAsJsonAsync(templates, new { name = "Cobro", purpose = 3, body = "Debes {vencido}" })).StatusCode);
        var ownersTemplate = await TwoBusinessesApi.IdAsync(mine.Client, templates, new { name = "Cobro", purpose = 3, body = "Hola {nombre}, debes {vencido}." });
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"{templates}/{ownersTemplate}",
            new { name = "Cobro", body = "Paga ya {vencido}", isActive = true })).StatusCode);

        // Without WhatsApp a business cannot turn them on; its settings are its own.
        var refused = await mine.Client.PutAsJsonAsync($"{Customers}/credit-reminders", new { enabled = true, everyDays = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var theirReminders = await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync($"{Customers}/credit-reminders", new { enabled = false, everyDays = 3 }));
        Assert.Equal(3, theirReminders.GetProperty("everyDays").GetInt32());
        var myReminders = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Customers}/credit-reminders"));
        Assert.Equal((false, 7, 1), (myReminders.GetProperty("enabled").GetBoolean(), myReminders.GetProperty("everyDays").GetInt32(),
            myReminders.GetProperty("customersWithOwnTerm").GetInt32()));
        Assert.Equal((1, 1), (
            await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CustomerCreditTermUpdated")),
            await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CustomerCreditTermUpdated"))));
        Assert.Equal((0, 1), (
            await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CreditRemindersUpdated")),
            await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(l => l.Action == "CreditRemindersUpdated"))));
    }

    private static object CreditSale(Guid product, Guid customer) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        customerId = customer,
        onCredit = true,
        items = new[] { Line(product) },
        payments = Array.Empty<object>()
    };

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    /// <summary>A member of the owner's team with the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole = 3,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }
}
