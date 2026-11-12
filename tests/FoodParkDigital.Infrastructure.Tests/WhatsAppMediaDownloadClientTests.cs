using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Downloading from Meta a photo or audio a customer sent (Fase 31L, ADR 0092): its address first, then its content, both with
/// the business's token, which only goes to Meta's own hosts; never bigger than asked; what Meta refuses is unavailable and
/// what fails now is tried again.
/// </summary>
public class WhatsAppMediaDownloadClientTests
{
    private const string Token = "token-del-negocio";
    private const string FileUrl = "https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=1029384756";
    private static readonly WhatsAppSenderAccount Account = new("2233445566", Token);

    [Fact]
    public async Task AFileOfTheCustomer_ShouldBeDownloaded_WithTheTokenOnlyToMeta()
    {
        var content = new byte[] { 0x4F, 0x67, 0x67, 0x53, 1, 2, 3 };
        var http = new Routes()
            .On("https://graph.facebook.com/v21.0/1029384756", HttpStatusCode.OK, $$"""{"url":"{{FileUrl}}","mime_type":"audio/ogg","file_size":7}""")
            .On(FileUrl, HttpStatusCode.OK, content);

        var download = await Client(http).DownloadMediaAsync(Account, "1029384756", 1024, CancellationToken.None);

        Assert.Equal(WhatsAppMediaDownloadOutcome.Downloaded, download.Outcome);
        Assert.Equal(content, download.Content);
        Assert.Equal(2, http.Requests.Count);
        Assert.All(http.Requests, r => Assert.Equal($"Bearer {Token}", r.Authorization));
    }

    [Theory]
    [InlineData("http://lookaside.fbsbx.com/media")]
    [InlineData("https://evil.example.com/media")]
    [InlineData("https://lookaside.fbsbx.com.evil.example.com/media")]
    [InlineData("https://lookaside.fbsbx.com:8443/media")]
    public async Task AnAddressOutsideMetasServers_ShouldBeUnavailable_WithoutSendingTheToken(string url)
    {
        var http = new Routes()
            .On("https://graph.facebook.com/v21.0/55", HttpStatusCode.OK, $$"""{"url":"{{url}}","file_size":10}""")
            .On(url, HttpStatusCode.OK, new byte[] { 1 });

        var download = await Client(http).DownloadMediaAsync(Account, "55", 1024, CancellationToken.None);

        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, download.Outcome);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task AFileBiggerThanAsked_ShouldBeUnavailable_ByItsSizeOrByWhatCame()
    {
        var bySize = new Routes()
            .On("https://graph.facebook.com/v21.0/56", HttpStatusCode.OK, $$"""{"url":"{{FileUrl}}","file_size":2048}""");
        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, (await Client(bySize).DownloadMediaAsync(Account, "56", 1024, CancellationToken.None)).Outcome);
        Assert.Single(bySize.Requests);

        // Its size said less than what came.
        var byContent = new Routes()
            .On("https://graph.facebook.com/v21.0/57", HttpStatusCode.OK, $$"""{"url":"{{FileUrl}}","file_size":10}""")
            .On(FileUrl, HttpStatusCode.OK, new byte[2048], withLength: false);
        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, (await Client(byContent).DownloadMediaAsync(Account, "57", 1024, CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, WhatsAppMediaDownloadOutcome.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, WhatsAppMediaDownloadOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, WhatsAppMediaDownloadOutcome.Transient)]
    [InlineData(HttpStatusCode.BadGateway, WhatsAppMediaDownloadOutcome.Transient)]
    public async Task WhatMetaAnswers_ShouldSayWhetherToTryAgain(HttpStatusCode status, WhatsAppMediaDownloadOutcome expected)
    {
        var address = new Routes().On("https://graph.facebook.com/v21.0/58", status, """{"error":{"code":100}}""");
        Assert.Equal(expected, (await Client(address).DownloadMediaAsync(Account, "58", 1024, CancellationToken.None)).Outcome);

        var file = new Routes()
            .On("https://graph.facebook.com/v21.0/59", HttpStatusCode.OK, $$"""{"url":"{{FileUrl}}","file_size":10}""")
            .On(FileUrl, status, Array.Empty<byte>());
        Assert.Equal(expected, (await Client(file).DownloadMediaAsync(Account, "59", 1024, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task NoConnection_ShouldBeTriedAgain_AndAnAnswerWithoutAddressIsUnavailable()
    {
        var down = new Routes { Fail = true };
        Assert.Equal(WhatsAppMediaDownloadOutcome.Transient, (await Client(down).DownloadMediaAsync(Account, "60", 1024, CancellationToken.None)).Outcome);

        var odd = new Routes().On("https://graph.facebook.com/v21.0/61", HttpStatusCode.OK, "{}");
        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, (await Client(odd).DownloadMediaAsync(Account, "61", 1024, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task TheDownloader_ShouldUseTheWhatsAppOfTheBusinessInScope_AndNothingWithoutIt()
    {
        var tenantId = Guid.NewGuid();
        var scope = new InMemoryApplicationDbContext.TestCurrentTenantService();
        scope.SetTenant(tenantId);
        await using var context = InMemoryApplicationDbContext.Create(scope);
        var http = new Routes()
            .On("https://graph.facebook.com/v21.0/62", HttpStatusCode.OK, $$"""{"url":"{{FileUrl}}","file_size":3}""")
            .On(FileUrl, HttpStatusCode.OK, new byte[] { 1, 2, 3 });
        var downloader = new WhatsAppMediaDownloader(context, Client(http));

        // Not connected yet: nothing goes to Meta.
        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, (await downloader.DownloadAsync("62", 1024, CancellationToken.None)).Outcome);
        var config = new WhatsAppConfig(tenantId);
        config.Update(isEnabled: false, accessToken: Token, phoneNumberId: "2233445566", wabaId: null, displayPhoneNumber: null);
        context.WhatsAppConfigs.Add(config);
        await context.SaveChangesAsync();
        Assert.Equal(WhatsAppMediaDownloadOutcome.Unavailable, (await downloader.DownloadAsync("62", 1024, CancellationToken.None)).Outcome);
        Assert.Empty(http.Requests);

        config.Update(isEnabled: true, accessToken: Token, phoneNumberId: "2233445566", wabaId: null, displayPhoneNumber: null);
        await context.SaveChangesAsync();
        var download = await downloader.DownloadAsync("62", 1024, CancellationToken.None);
        Assert.Equal((WhatsAppMediaDownloadOutcome.Downloaded, 3), (download.Outcome, download.Content!.Length));
        Assert.All(http.Requests, r => Assert.Equal($"Bearer {Token}", r.Authorization));
    }

    private static WhatsAppCloudClient Client(Routes http)
        => new(http, new TestHostEnvironment(Environments.Production), NullLogger<WhatsAppCloudClient>.Instance);

    /// <summary>Answers each address as set, and records who asked with which token.</summary>
    private sealed class Routes : HttpMessageHandler, IHttpClientFactory
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body, bool WithLength)> _routes = new();

        public bool Fail { get; init; }

        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];

        public Routes On(string url, HttpStatusCode status, string body) => On(url, status, System.Text.Encoding.UTF8.GetBytes(body));

        public Routes On(string url, HttpStatusCode status, byte[] body, bool withLength = true)
        {
            _routes[url] = (status, body, withLength);
            return this;
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            if (Fail)
                throw new HttpRequestException("Sin conexión");
            if (!_routes.TryGetValue(request.RequestUri!.ToString(), out var route))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            HttpContent content = route.WithLength
                ? new ByteArrayContent(route.Body)
                : new StreamContent(new UnknownLengthStream(route.Body));
            return Task.FromResult(new HttpResponseMessage(route.Status) { Content = content });
        }
    }

    /// <summary>A stream that does not say how long it is, as a response without Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] body) : MemoryStream(body)
    {
        public override bool CanSeek => false;
    }
}
