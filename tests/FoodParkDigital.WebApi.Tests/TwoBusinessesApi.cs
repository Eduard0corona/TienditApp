using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Security;
using FoodParkDigital.WebApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>The suites that share the two businesses: one API and one database, one test at a time.</summary>
[CollectionDefinition(Name)]
public sealed class TwoBusinessesCollection : ICollectionFixture<TwoBusinessesApi>
{
    public const string Name = "Two businesses on the real API";
}

/// <summary>A business registered through the API, its owner's client and session, and the records it created.</summary>
public sealed record Business(
    Guid TenantId,
    HttpClient Client,
    string AccessToken,
    Guid CategoryId,
    Guid ProductId,
    Guid CustomerId,
    Guid SupplierId,
    Guid OrderId,
    string ReceiptPath,
    Guid LocationId,
    Guid SupplyId,
    Guid MovementId,
    Guid PurchaseOrderId,
    Guid PurchaseOrderLineId,
    Guid CashSessionId,
    Guid AppointmentId,
    string WhatsAppPhoneNumberId);

/// <summary>
/// An owner who signed up, activated the account from the emailed link and signed in: the client, the access token
/// and the cookie of the session, and the email.
/// </summary>
public sealed record SignedUp(Guid TenantId, HttpClient Client, string AccessToken, string RefreshCookie, string Email);

/// <summary>
/// The API in process on a throwaway SQL Server database, with two businesses registered through it. Hubs check their
/// connections every second, and WhatsApp deliveries are signed with a test app secret.
/// </summary>
public sealed class TwoBusinessesApi : IAsyncLifetime
{
    public const int FoodBusiness = 0;
    public const int ServicesBusiness = 2;
    private const string Password = "Secreto1";

    private readonly string _privateFiles = Path.Combine(Path.GetTempPath(), "fpd-http-tests", Guid.NewGuid().ToString("N"));
    private readonly RecordedEmails _emails = new();
    private SqlServerTestDatabase? _database;
    private WebApplicationFactory<Program>? _factory;

    public Business A { get; private set; } = null!;
    public Business B { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestDatabase.ConnectionVariable)))
            return;

        _database = await SqlServerTestDatabase.CreateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            // Development: no secrets to configure (the signing key is generated) and no rate limit on sign-ups.
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:DefaultConnection", _database.ConnectionString);
            host.UseSetting("Database:AutoMigrateOnStartup", "false");
            host.UseSetting("Database:SeedOnStartup", "false");
            host.UseSetting("BackgroundJobs:Enabled", "false");
            host.UseSetting("FileStorage:Provider", "Local");
            host.UseSetting("FileStorage:Local:PrivateRootPath", _privateFiles);
            // appsettings.json carries placeholder SMTP and SMS settings: without a host or an account, they only log
            // what they would send. The emails are recorded instead: signing in needs the link of the sign-up email.
            host.UseSetting("EmailSettings:Smtp:Host", "");
            host.UseSetting("SmsSettings:AccountSid", "");
            host.ConfigureTestServices(services => services.AddSingleton<IEmailService>(_emails));
            // A hub connection whose session ended closes within a second, not the default 30.
            host.UseSetting("Hubs:AccessCheckInterval", "00:00:01");
            host.UseSetting(WhatsAppWebhookSignatureVerifier.AppSecretSetting, WhatsAppWebhookTestSupport.AppSecret);
            host.UseSetting(WhatsAppWebhookSignatureVerifier.AllowUnsignedWebhooksSetting, "false");
        });

        // The roles the seed of a new database creates (accounts are registered with them).
        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var role in new[] { "SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN", "CUSTOMER" })
                await roles.CreateAsync(new ApplicationRole(role));
        }

        A = await RegisterAsync("a", whatsAppPhoneNumberId: "100000000001");
        B = await RegisterAsync("b", whatsAppPhoneNumberId: "100000000002");
    }

    public async Task DisposeAsync()
    {
        A?.Client.Dispose();
        B?.Client.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
        if (Directory.Exists(_privateFiles))
            Directory.Delete(_privateFiles, recursive: true);
    }

    /// <summary>
    /// Registers a business, activates its owner's account from the emailed link and signs the owner in. Of services
    /// by default: it sells, keeps stock and books appointments (a <see cref="FoodBusiness"/> sells with tables and a
    /// kitchen instead).
    /// </summary>
    public async Task<SignedUp> SignUpAsync(string name, int businessType = ServicesBusiness)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"dueno-{name}-{suffix}@demo.com";
        var response = await client.PostAsJsonAsync("/api/v1/auth/register/business", new
        {
            email,
            password = Password,
            firstName = "Dueño",
            lastName = name.ToUpperInvariant(),
            phoneNumber = "+52155" + Random.Shared.Next(10000000, 99999999),
            businessName = $"Negocio {name.ToUpperInvariant()}",
            businessType
        });
        var registered = await ReadAsync(response);
        // Signing up opens no session: no tokens and no refresh cookie until the account is activated.
        Assert.False(registered.TryGetProperty("accessToken", out _));
        Assert.DoesNotContain(SetCookies(response), cookie => cookie.StartsWith($"{RefreshTokenCookie.Name}=", StringComparison.Ordinal));

        // The owner opens the emailed link (the app's /confirm-email sends it to this endpoint) and then signs in.
        var (userId, token) = _emails.ConfirmationTo(email);
        await ReadAsync(await client.GetAsync(
            $"/api/v1/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}"));
        var signIn = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var signedIn = await ReadAsync(signIn);
        var accessToken = signedIn.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var refreshCookie = SetCookies(signIn)
            .Single(cookie => cookie.StartsWith($"{RefreshTokenCookie.Name}=", StringComparison.Ordinal));

        return new SignedUp(signedIn.GetProperty("user").GetProperty("tenantId").GetGuid(), client, accessToken, refreshCookie, email);
    }

    private static IEnumerable<string> SetCookies(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var headers) ? headers.Select(header => header.Split(';')[0]) : [];

    /// <summary>
    /// Makes the account a platform admin (only the database can) and signs it in again: a client whose token carries
    /// that role and the business the account belongs to.
    /// </summary>
    public async Task<HttpClient> SignInAsPlatformAdminAsync(SignedUp account)
    {
        using (var scope = _factory!.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(account.Email);
            user!.Role = UserRole.SuperAdmin;
            var updated = await users.UpdateAsync(user);
            Assert.True(updated.Succeeded, string.Join("; ", updated.Errors.Select(e => e.Description)));
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var signedIn = await ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Signs the session out, like the app does: with its refresh cookie.</summary>
    public async Task<HttpResponseMessage> SignOutAsync(SignedUp account)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", account.RefreshCookie);
        return await account.Client.SendAsync(request);
    }

    /// <summary>A connection to a hub over WebSockets (the transport only the access sweep can close), not started.</summary>
    public HubConnection Hub(string path, string accessToken) => new HubConnectionBuilder()
        .WithUrl(new Uri(_factory!.Server.BaseAddress, path), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            options.WebSocketFactory = async (context, cancellationToken) =>
            {
                var webSockets = _factory.Server.CreateWebSocketClient();
                webSockets.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {accessToken}";
                return await webSockets.ConnectAsync(context.Uri, cancellationToken);
            };
        })
        .Build();

    public async Task<Guid> CreateOrderAsync(Business business) =>
        await IdAsync(business.Client, "/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            openTab = false,
            customerName = "Mostrador",
            items = new[] { new { productId = business.ProductId, quantity = 1, optionIds = Array.Empty<Guid>() } },
            payments = Array.Empty<object>()
        });

    /// <summary>Posts a WhatsApp delivery as Meta does: the raw body signed with the app secret (or the given signature).</summary>
    public async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature = "valid")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/whatsapp/webhook")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (signature is not null)
        {
            request.Headers.Add(
                WhatsAppWebhookSignatureVerifier.SignatureHeaderName,
                signature == "valid" ? WhatsAppWebhookTestSupport.Sign(bytes, WhatsAppWebhookTestSupport.AppSecret) : signature);
        }

        using var client = _factory!.CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Reads the database as one business sees it (with its filter), for what the API never shows: rows a refused
    /// request may have saved before failing.
    /// </summary>
    public async Task<T> QueryAsync<T>(Guid tenantId, Func<ApplicationDbContext, Task<T>> query)
    {
        await using var context = _database!.CreateContext(tenantId);
        return await query(context);
    }

    /// <summary>What the inbox worker does on each pass: processes the stored deliveries that are due.</summary>
    public Task<int> ProcessWebhookInboxAsync() =>
        _factory!.Services.GetRequiredService<InboxProcessor>().ProcessDueAsync(CancellationToken.None);

    /// <summary>A text message from a customer to one WhatsApp number, as Meta delivers it.</summary>
    public static string InboundMessage(string phoneNumberId, string fromPhone, string text) =>
        JsonSerializer.Serialize(new
        {
            @object = "whatsapp_business_account",
            entry = new[]
            {
                new
                {
                    id = "waba-pruebas",
                    changes = new[]
                    {
                        new
                        {
                            field = "messages",
                            value = new
                            {
                                messaging_product = "whatsapp",
                                metadata = new { display_phone_number = "5215500000000", phone_number_id = phoneNumberId },
                                contacts = new[] { new { profile = new { name = "Cliente" }, wa_id = fromPhone } },
                                messages = new[]
                                {
                                    new
                                    {
                                        from = fromPhone,
                                        id = $"wamid.{Guid.NewGuid():N}",
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                                        type = "text",
                                        text = new { body = text }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        });

    private async Task<Business> RegisterAsync(string name, string whatsAppPhoneNumberId)
    {
        var owner = await SignUpAsync(name);
        var client = owner.Client;
        var upper = name.ToUpperInvariant();

        var category = await IdAsync(client, "/api/v1/categories", new { name = "Bebidas", sortOrder = 1 });
        var product = await IdAsync(client, "/api/v1/products", CrossTenantHttpSqlServerTests.NewProduct(category, $"Agua de {upper}"));
        var customer = await IdAsync(client, "/api/v1/customers", new { name = $"Cliente de {upper}", phone = "5511112222" });
        var supplier = await IdAsync(client, "/api/v1/suppliers", new { name = $"Proveedor de {upper}" });
        var business = new Business(
            owner.TenantId, client, owner.AccessToken, category, product, customer, supplier, Guid.Empty, "",
            Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, whatsAppPhoneNumberId);
        var order = await CreateOrderAsync(business);

        using var receipt = new MultipartFormDataContent();
        var png = new ByteArrayContent(TinyPng);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        receipt.Add(png, "file", "recibo.png");
        var uploaded = await ReadAsync(await client.PostAsync("/api/v1/orders/uploads/payment-evidence", receipt));

        // Stock: the warehouse every business starts with, a supply and an entry of 10.
        var locations = await ReadAsync(await client.GetAsync("/api/v1/inventory/locations"));
        var location = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var supply = await IdAsync(client, "/api/v1/inventory/supplies", new { name = $"Harina de {upper}", unit = 0, defaultCost = 10m });
        var movement = await IdAsync(client, "/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = location, quantity = 10m, unitCost = 10m } }
        });

        // A purchase order sent to its supplier and not received yet.
        var purchaseOrder = await ReadAsync(await client.PostAsJsonAsync("/api/v1/purchaseorders", new
        {
            supplierId = supplier,
            markAsSent = true,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = location, quantity = 5m, unitCost = 10m, taxPercent = 0m } }
        }));

        // The owner's open drawer and an appointment for tomorrow.
        var cashSession = await IdAsync(client, "/api/v1/cash/open", new { openingAmount = 100m });
        var tomorrow = DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(16);
        var appointment = await IdAsync(client, "/api/v1/appointments", new
        {
            customerName = $"Cliente de {upper}",
            customerPhone = "5511112222",
            serviceName = "Corte",
            startAt = new DateTimeOffset(tomorrow, TimeSpan.Zero),
            endAt = new DateTimeOffset(tomorrow.AddHours(1), TimeSpan.Zero)
        });

        // Its WhatsApp number routes the webhook's deliveries to it. Left disabled: nothing is sent to Meta.
        await ReadAsync(await client.PutAsJsonAsync("/api/v1/auth/my-tenant/whatsapp", new
        {
            isEnabled = false,
            phoneNumberId = whatsAppPhoneNumberId,
            displayPhoneNumber = $"+52 55 0000 {whatsAppPhoneNumberId[^4..]}"
        }));

        return business with
        {
            OrderId = order,
            ReceiptPath = uploaded.GetProperty("url").GetString()!,
            LocationId = location,
            SupplyId = supply,
            MovementId = movement,
            PurchaseOrderId = purchaseOrder.GetProperty("id").GetGuid(),
            PurchaseOrderLineId = purchaseOrder.GetProperty("lines")[0].GetProperty("id").GetGuid(),
            CashSessionId = cashSession,
            AppointmentId = appointment
        };
    }

    internal static async Task<Guid> IdAsync(HttpClient client, string path, object body)
        => (await ReadAsync(await client.PostAsJsonAsync(path, body))).GetProperty("id").GetGuid();

    internal static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    // A 1x1 PNG: the upload validates the real content and re-encodes it.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>The emails the API sends, kept to open their links as the person would.</summary>
    private sealed class RecordedEmails : IEmailService
    {
        private readonly ConcurrentQueue<(string Email, string UserId, string Token)> _confirmations = new();

        /// <summary>What the link of the one confirmation email sent to <paramref name="email"/> carries.</summary>
        public (string UserId, string Token) ConfirmationTo(string email)
        {
            var sent = _confirmations.Where(confirmation => confirmation.Email == email).ToList();
            Assert.True(sent.Count == 1, $"Expected one confirmation email to {email}, got {sent.Count}.");
            return (sent[0].UserId, sent[0].Token);
        }

        public Task SendEmailConfirmationAsync(string email, string userId, string token)
        {
            _confirmations.Enqueue((email, userId, token));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string email, string userId, string token) => Task.CompletedTask;

        public Task SendEmailAsync(string to, string subject, string body) => Task.CompletedTask;

        public Task SendTeamInvitationAsync(string email, string businessName, string invitationPath) => Task.CompletedTask;
    }
}
