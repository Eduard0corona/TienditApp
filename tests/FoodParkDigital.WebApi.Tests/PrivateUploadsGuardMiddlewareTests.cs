using FoodParkDigital.WebApi.Middleware;
using Microsoft.AspNetCore.Http;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>Receipts and service photos that still sit under wwwroot/uploads are never static files.</summary>
public class PrivateUploadsGuardMiddlewareTests
{
    [Theory]
    [InlineData("/uploads/orders/t/payment-evidence/202610/f.webp")]
    [InlineData("/uploads/Orders/t/service-intake/202610/f.webp")]
    [InlineData("/UPLOADS/orders/t/payment-evidence/202610/f.pdf")]
    // Kestrel removes "." and ".." but keeps empty segments and backslashes, and the file provider reads them as the
    // same folder (a backslash is a separator on Windows).
    [InlineData("/uploads//orders/t/payment-evidence/202610/f.webp")]
    [InlineData("//uploads/orders/t/payment-evidence/202610/f.webp")]
    [InlineData("/uploads/orders//t/payment-evidence/202610/f.webp")]
    [InlineData("/uploads\\orders\\t\\payment-evidence\\202610\\f.webp")]
    [InlineData("/uploads/orders")]
    public async Task PrivateFolders_ShouldAnswerNotFound(string path)
    {
        var (context, nextCalled) = await InvokeAsync(path);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled());
    }

    [Theory]
    [InlineData("/uploads/products/t/foto.webp")]
    [InlineData("/uploads/ordersheet.png")]
    [InlineData("/assets/index.js")]
    public async Task OtherFiles_ShouldBeServedAsBefore(string path)
    {
        var (_, nextCalled) = await InvokeAsync(path);

        Assert.True(nextCalled());
    }

    private static async Task<(HttpContext Context, Func<bool> NextCalled)> InvokeAsync(string path)
    {
        var called = false;
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var middleware = new PrivateUploadsGuardMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        return (context, () => called);
    }
}
