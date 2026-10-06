using System.Net.Http.Json;
using System.Reflection;
using FoodParkDigital.WebApi.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Reads stop when their client leaves; changes finish once they arrived, so a closed page never leaves half a change
/// (an account without its business, a new password with the old sessions still open).
/// </summary>
public class RequestCancellationTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void Reads_ShouldStopWhenTheClientLeaves(string method)
    {
        using var client = new CancellationTokenSource();
        var context = Request(method, client.Token);

        var token = RequestCancellation.For(context);
        client.Cancel();

        Assert.True(token.IsCancellationRequested);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Changes_ShouldFinishEvenIfTheClientLeaves(string method)
    {
        using var client = new CancellationTokenSource();
        client.Cancel();

        var token = RequestCancellation.For(Request(method, client.Token));

        Assert.False(token.CanBeCanceled);
    }

    [Fact]
    public async Task Actions_ShouldReceiveIt_ThroughModelBinding()
    {
        await using var app = await ProbeAppAsync();
        using var http = app.GetTestClient();

        // The framework would give both the request's cancellation, which the test server can cancel.
        Assert.True(await http.GetFromJsonAsync<bool>("/probe"));
        Assert.False(await (await http.PostAsync("/probe", null)).Content.ReadFromJsonAsync<bool>());
    }

    [Fact]
    public async Task TheApi_ShouldBindEveryActionsTokenThroughIt()
    {
        // The API as it starts, without a database (nothing reaches it to read its options).
        await using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1,1;Database=SinBase;Encrypt=False");
            host.UseSetting("Database:AutoMigrateOnStartup", "false");
            host.UseSetting("Database:SeedOnStartup", "false");
            host.UseSetting("BackgroundJobs:Enabled", "false");
        });

        var mvc = api.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        Assert.IsType<RequestCancellation.BinderProvider>(mvc.ModelBinderProviders[0]);
    }

    private static DefaultHttpContext Request(string method, CancellationToken aborted)
    {
        var context = new DefaultHttpContext { RequestAborted = aborted };
        context.Request.Method = method;
        return context;
    }

    /// <summary>An app with only <see cref="ProbeController"/>, bound as the API binds.</summary>
    private static async Task<WebApplication> ProbeAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers(RequestCancellation.Apply)
            .ConfigureApplicationPartManager(parts =>
            {
                parts.ApplicationParts.Clear();
                parts.FeatureProviders.Add(new OnlyTheProbe());
            });
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class OnlyTheProbe : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
            => feature.Controllers.Add(typeof(ProbeController).GetTypeInfo());
    }

    /// <summary>Answers whether the token it received can be cancelled.</summary>
    [ApiController]
    [Route("probe")]
    public sealed class ProbeController : ControllerBase
    {
        [HttpGet]
        public bool Read(CancellationToken cancellationToken) => cancellationToken.CanBeCanceled;

        [HttpPost]
        public bool Change(CancellationToken cancellationToken) => cancellationToken.CanBeCanceled;
    }
}
