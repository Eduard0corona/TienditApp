using System.Reflection;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Roles accepted by the endpoints aligned with the permission matrix. TenantResolutionMiddleware checks
/// the permission code; a narrower role list here made the menu offer screens that answered 403.
/// </summary>
public class ControllerRoleAuthorizationTests
{
    [Theory]
    [InlineData(typeof(DashboardController), null, "STAFF", true)]
    [InlineData(typeof(DashboardController), null, "KITCHEN", false)]
    [InlineData(typeof(ReportsController), null, "STAFF", true)]
    [InlineData(typeof(KitchenController), null, "KITCHEN", true)]
    [InlineData(typeof(KitchenController), null, "STAFF", true)]
    [InlineData(typeof(KitchenController), null, "TENANT_ADMIN", true)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.UpdateStatus), "STAFF", true)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.UpdateStatus), "KITCHEN", false)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.Cancel), "STAFF", true)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.Cancel), "KITCHEN", false)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.Create), "STAFF", true)]
    [InlineData(typeof(OrdersController), nameof(OrdersController.AddPayment), "STAFF", true)]
    [InlineData(typeof(WhatsAppChatController), nameof(WhatsAppChatController.SendMessage), "STAFF", true)]
    [InlineData(typeof(WhatsAppChatController), nameof(WhatsAppChatController.SendMessage), "KITCHEN", false)]
    // Permissions offered to staff work for staff: the request still checks the permission itself.
    [InlineData(typeof(AiAgentsController), nameof(AiAgentsController.GetAll), "STAFF", true)]
    [InlineData(typeof(AiAgentsController), nameof(AiAgentsController.GetAll), "KITCHEN", false)]
    [InlineData(typeof(CategoriesController), nameof(CategoriesController.Create), "STAFF", true)]
    [InlineData(typeof(ProductsController), nameof(ProductsController.Delete), "STAFF", true)]
    // The team stays with owners and admins: managing permissions would let anyone give themselves all of them.
    [InlineData(typeof(EmployeesController), nameof(EmployeesController.Create), "STAFF", false)]
    [InlineData(typeof(EmployeesController), nameof(EmployeesController.ResendInvitation), "TENANT_ADMIN", true)]
    public void Endpoint_ShouldAcceptTheRolesOfThePermissionMatrix(Type controller, string? action, string role, bool expected)
    {
        Assert.Equal(expected, AllowsRole(controller, action, role));
    }

    // ASP.NET Core requires every [Authorize] that applies (controller and action) to be satisfied.
    private static bool AllowsRole(Type controller, string? action, string role)
    {
        var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        if (action is not null)
        {
            var method = controller.GetMethod(action)
                ?? throw new InvalidOperationException($"{controller.Name}.{action} does not exist.");
            attributes.AddRange(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
        }

        return attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Roles))
            .All(attribute => attribute.Roles!
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(role));
    }
}
