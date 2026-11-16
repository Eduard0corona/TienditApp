using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Assistants;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Mcp;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// An AI assistant with the customer's account (Fase 32E, ADR 0097), through the API as Claude does it, with the real handlers
/// on SQL Server: it registers, the browser goes to the permission page, the customer allows it with their session, the code
/// becomes tokens once, and /mcp/cuenta reads the account's addresses and orders and prepares one of them again. No session
/// of the app opens /mcp/cuenta; Mi cuenta lists the assistant and disconnecting it closes the next request.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class AssistantAccountSqlServerTests
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    // RFC 7636, appendix B.
    private const string Verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private readonly TwoBusinessesApi _api;
    private int _id;

    public AssistantAccountSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task AnAssistant_ShouldUseTheAccount_TheCustomerAllowed_UntilTheyDisconnectIt()
    {
        // A taquería that takes orders, and a customer who saved their home and ordered there.
        var owner = await _api.SignUpAsync("cuenta-asistente", TwoBusinessesApi.FoodBusiness);
        var tacos = await ProductAsync(owner.Client, "Tacos de canasta");
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 2 })))
            .GetProperty("slug").GetString()!;
        var (_, customer, _) = await _api.SignUpCustomerAsync("asistente");
        await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/me/addresses",
            new { label = "Casa", postalCode = "06700", addressLine = "Colima 145, Roma Norte" }));
        var placed = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{slug}/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345601",
            items = new[] { new { productId = tacos, quantity = 2 } }
        }));
        var orderNumber = placed.GetProperty("orderNumber").GetString()!;
        using var assistant = _api.AnonymousClient(followRedirects: false);

        // Without a token, /mcp/cuenta says where to learn how to get one; that metadata names the resource and its issuer.
        using (var unauthorized = await McpAsync(assistant, McpServer.AccountPath, "tools/list", new JsonObject(), token: null))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var resourceMetadata = await JsonAsync(await assistant.GetAsync(AssistantOAuth.ProtectedResourcePath));
        var resource = (string)resourceMetadata["resource"]!;
        Assert.EndsWith(McpServer.AccountPath, resource);

        // It registers, and sends the browser to authorize: the permission page gets the same request.
        var registered = await JsonAsync(await assistant.PostAsJsonAsync(AssistantOAuth.RegisterPath, new Dictionary<string, object>
        {
            ["client_name"] = "Claude",
            ["redirect_uris"] = new[] { Callback },
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" }
        }), HttpStatusCode.Created);
        var clientId = (string)registered["client_id"]!;
        var query = $"response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(Callback)}&code_challenge={Challenge}" +
            $"&code_challenge_method=S256&resource={Uri.EscapeDataString(resource)}&scope=cuenta&state=abc";
        using (var authorize = await assistant.GetAsync($"{AssistantOAuth.AuthorizePath}?{query}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
            Assert.Equal($"{AssistantOAuth.ConsentPath}?{query}", authorize.Headers.Location!.OriginalString);
        }

        // The page shows who asks and where it returns; the owner of a business cannot allow it, the customer can.
        var check = await TwoBusinessesApi.ReadAsync(await assistant.GetAsync(
            $"/api/v1/auth/assistant-authorizations?responseType=code&clientId={clientId}&redirectUri={Uri.EscapeDataString(Callback)}" +
            $"&codeChallenge={Challenge}&codeChallengeMethod=S256&resource={Uri.EscapeDataString(resource)}&scope=cuenta&state=abc"));
        Assert.Equal((true, "Claude", "claude.ai"), (check.GetProperty("valid").GetBoolean(),
            check.GetProperty("clientName").GetString(), check.GetProperty("redirectSite").GetString()));
        var answer = new
        {
            responseType = "code", clientId, redirectUri = Callback, codeChallenge = Challenge, codeChallengeMethod = "S256",
            resource, scope = "cuenta", state = "abc", allow = true
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PostAsJsonAsync("/api/v1/auth/assistant-authorizations", answer)).StatusCode);
        var redirect = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/auth/assistant-authorizations", answer)))
            .GetProperty("redirectUrl").GetString()!;
        var code = Regex.Match(redirect, "[?&]code=([A-Za-z0-9_-]{43})").Groups[1].Value;
        Assert.StartsWith(Callback + "?code=", redirect);
        Assert.EndsWith("&state=abc", redirect);

        // The code becomes tokens once, with its verifier.
        var tokens = await TokenAsync(assistant, HttpStatusCode.OK,
            ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", Callback), ("client_id", clientId),
            ("code_verifier", Verifier), ("resource", resource));
        Assert.Equal(("Bearer", 3600, "cuenta"), ((string)tokens["token_type"]!, (int)tokens["expires_in"]!, (string)tokens["scope"]!));
        var access = (string)tokens["access_token"]!;
        var refresh = (string)tokens["refresh_token"]!;
        var again = await TokenAsync(assistant, HttpStatusCode.BadRequest,
            ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", Callback), ("client_id", clientId), ("code_verifier", Verifier));
        Assert.Equal("invalid_grant", (string?)again["error"]);
        // The second exchange ended the connection the first one made: allowed again, it works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(assistant, McpServer.AccountPath, "tools/list", new JsonObject(), access)).StatusCode);
        redirect = (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync("/api/v1/auth/assistant-authorizations", answer)))
            .GetProperty("redirectUrl").GetString()!;
        tokens = await TokenAsync(assistant, HttpStatusCode.OK,
            ("grant_type", "authorization_code"), ("code", Regex.Match(redirect, "[?&]code=([A-Za-z0-9_-]{43})").Groups[1].Value),
            ("redirect_uri", Callback), ("client_id", clientId), ("code_verifier", Verifier));
        access = (string)tokens["access_token"]!;
        refresh = (string)tokens["refresh_token"]!;
        // Only their hashes are kept.
        var hashes = await _api.QueryAsync(owner.TenantId, db => db.AssistantTokens.Select(t => t.TokenHash).ToListAsync());
        Assert.Contains(AssistantSecrets.HashOf(access), hashes);
        Assert.DoesNotContain(access, hashes);

        // With the account: its addresses, its orders, and one of them prepared again with today's menu.
        var addresses = await ToolAsync(assistant, "mis_direcciones", new JsonObject(), access);
        Assert.Contains("- Casa: código postal 06700 · Colima 145, Roma Norte", addresses);
        var orders = await ToolAsync(assistant, "mis_pedidos", new JsonObject(), access);
        Assert.Contains(orderNumber, orders);
        Assert.Contains("2 × Tacos de canasta", orders);
        var orderId = Regex.Match(orders, "- id: `([0-9a-f-]{36})`").Groups[1].Value;
        var prepared = await ToolAsync(assistant, "pedir_otra_vez", new JsonObject { ["pedido"] = orderId, ["codigo_postal"] = "06700" }, access);
        Assert.Contains("Todavía no se envía.", prepared);
        Assert.Contains("- 2 × Tacos de canasta", prepared);
        Assert.Matches($"Enlace: .+/m/{Regex.Escape(slug)}#pedido=[A-Za-z0-9_-]{{43}}", prepared);
        Assert.Single(await _api.QueryAsync(owner.TenantId, db => db.AssistantOrders.ToListAsync()));
        Assert.Contains("no tiene negocios favoritos", await ToolAsync(assistant, "mis_favoritos", new JsonObject(), access));

        // No session of the app opens it: neither the business's nor the customer's own.
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(assistant, McpServer.AccountPath, "tools/list", new JsonObject(), owner.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await McpAsync(assistant, McpServer.AccountPath, "tools/list", new JsonObject(),
            customer.DefaultRequestHeaders.Authorization!.Parameter)).StatusCode);

        // It renews: a new pair, and the new access token works.
        var renewed = await TokenAsync(assistant, HttpStatusCode.OK, ("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", clientId));
        access = (string)renewed["access_token"]!;
        Assert.NotEqual(refresh, (string)renewed["refresh_token"]!);
        Assert.Contains("Casa", await ToolAsync(assistant, "mis_direcciones", new JsonObject(), access));

        // Mi cuenta lists it, and disconnecting it closes the next request.
        var connected = Assert.Single((await TwoBusinessesApi.ReadAsync(await customer.GetAsync("/api/v1/me/assistants"))).EnumerateArray());
        Assert.Equal(("Claude", "claude.ai"), (connected.GetProperty("name").GetString(), connected.GetProperty("site").GetString()));
        Assert.Equal(HttpStatusCode.NoContent,
            (await customer.DeleteAsync($"/api/v1/me/assistants/{connected.GetProperty("id").GetGuid()}")).StatusCode);
        using var closed = await McpAsync(assistant, McpServer.AccountPath, "tools/list", new JsonObject(), access);
        Assert.Equal(HttpStatusCode.Unauthorized, closed.StatusCode);
        Assert.Contains("error=\"invalid_token\"", closed.Headers.WwwAuthenticate.ToString());
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await customer.GetAsync("/api/v1/me/assistants"))).EnumerateArray());
    }

    [SqlServerFact]
    public async Task AuthorizeAndToken_ShouldReturnTheErrorsAnAssistantCanFix_AndSayTheRestOnThePage()
    {
        using var assistant = _api.AnonymousClient(followRedirects: false);
        var registered = await JsonAsync(await assistant.PostAsJsonAsync(AssistantOAuth.RegisterPath, new Dictionary<string, object>
        {
            ["client_name"] = "Inspector", ["redirect_uris"] = new[] { "http://localhost:6274/oauth/callback" }
        }), HttpStatusCode.Created);
        var clientId = (string)registered["client_id"]!;
        Assert.Equal("invalid_redirect_uri", (string?)(await JsonAsync(await assistant.PostAsJsonAsync(AssistantOAuth.RegisterPath,
            new Dictionary<string, object> { ["redirect_uris"] = new[] { "http://evil.example/cb" } }), HttpStatusCode.BadRequest))["error"]);

        // Without PKCE: back to the assistant, with its state. On this device the port may change.
        using (var withoutPkce = await assistant.GetAsync(
            $"{AssistantOAuth.AuthorizePath}?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString("http://localhost:50001/oauth/callback")}&state=s1"))
        {
            Assert.Equal(HttpStatusCode.Redirect, withoutPkce.StatusCode);
            Assert.StartsWith("http://localhost:50001/oauth/callback?error=invalid_request&", withoutPkce.Headers.Location!.OriginalString);
            Assert.EndsWith("&state=s1", withoutPkce.Headers.Location.OriginalString);
        }
        // A client that does not exist, or an address that is not its own: the page says it; nothing goes there.
        foreach (var wrong in new[]
        {
            $"response_type=code&client_id={new string('z', 43)}&redirect_uri={Uri.EscapeDataString(Callback)}&code_challenge={Challenge}&code_challenge_method=S256",
            $"response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString("https://evil.example/cb")}&code_challenge={Challenge}&code_challenge_method=S256"
        })
        {
            using var response = await assistant.GetAsync($"{AssistantOAuth.AuthorizePath}?{wrong}");
            Assert.StartsWith(AssistantOAuth.ConsentPath + "?", response.Headers.Location!.OriginalString);
        }

        Assert.Equal("unsupported_grant_type", (string?)(await TokenAsync(assistant, HttpStatusCode.BadRequest, ("grant_type", "password")))["error"]);
        Assert.Equal("invalid_grant", (string?)(await TokenAsync(assistant, HttpStatusCode.BadRequest,
            ("grant_type", "refresh_token"), ("refresh_token", new string('r', 43))))["error"]);
        Assert.Equal("invalid_target", (string?)(await TokenAsync(assistant, HttpStatusCode.BadRequest,
            ("grant_type", "refresh_token"), ("refresh_token", new string('r', 43)), ("resource", "https://otro.example/mcp")))["error"]);
        // Revoking answers the same for any token.
        using var revoked = await assistant.PostAsync(AssistantOAuth.RevokePath, new FormUrlEncodedContent([new("token", "nada")]));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
    }

    private static async Task<JsonNode> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var raw = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {raw}");
            return JsonNode.Parse(raw)!;
        }
    }

    private static async Task<JsonNode> TokenAsync(HttpClient client, HttpStatusCode expected, params (string Name, string Value)[] form)
    {
        var response = await client.PostAsync(AssistantOAuth.TokenPath,
            new FormUrlEncodedContent(form.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))));
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await JsonAsync(response, expected);
    }

    private async Task<HttpResponseMessage> McpAsync(HttpClient client, string path, string method, JsonObject parameters, string? token)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method, ["params"] = parameters };
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    /// <summary>One tool call to /mcp/cuenta with the access token; the text it answers.</summary>
    private async Task<string> ToolAsync(HttpClient client, string name, JsonObject arguments, string token)
    {
        using var response = await McpAsync(client, McpServer.AccountPath, "tools/call",
            new JsonObject { ["name"] = name, ["arguments"] = arguments }, token);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {raw}");
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? string.Join("\n", raw.Split('\n').Where(text => text.StartsWith("data:", StringComparison.Ordinal)).Select(text => text[5..].Trim()))
            : raw;
        var result = JsonNode.Parse(json)!["result"]!;
        Assert.True((bool?)result["isError"] is null or false, result.ToJsonString());
        return (string)result["content"]![0]!["text"]!;
    }
}
