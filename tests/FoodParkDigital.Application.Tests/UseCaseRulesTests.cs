using FoodParkDigital.Application.AiAgents.Queries;
using FoodParkDigital.Application.Audit.Queries;
using FoodParkDigital.Application.Branches;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Couriers;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Customers.Queries;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Dining;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Marketing.Commands;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Messaging.OptOuts;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Reports.Queries;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Application.WhatsApp.Commands;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Who may do each job is declared by its use case and checked by the authorization behavior: the middleware only
/// resolves the business and the membership, and the controllers ask for a signed-in user. These are the rules the
/// route map and the controllers' roles used to add, by permission and by module.
/// </summary>
public class UseCaseRulesTests
{
    public static TheoryData<Type, string> Jobs => new()
    {
        // Taking orders, the kitchen and the dining room.
        { typeof(CreateOrderCommand), "Permission(ManageOrders)" },
        { typeof(CreateTenantOrderCommand), "Permission(ManageOrders)" },
        { typeof(UpdateOrderStatusCommand), "Permission(ManageOrders)" },
        { typeof(CancelOrderCommand), "Permission(ManageOrders)" },
        { typeof(MoveKitchenOrderCommand), "Permission(ManageServiceBoard) Module(Kitchen)" },
        { typeof(UpdateKitchenTicketStatusCommand), "Permission(ManageServiceBoard) Module(Kitchen)" },
        { typeof(GetKitchenOrdersQuery), "Permission(ManageServiceBoard) Module(Kitchen)" },
        { typeof(GetKitchenTicketsQuery), "Permission(ManageServiceBoard) Module(Kitchen)" },
        { typeof(MarkRoundServedCommand), "Permission(ManageOrders) Module(Food)" },
        { typeof(CancelTabItemCommand), "Permission(ManageOrders) Module(Food)" },
        { typeof(GetDiningRoomQuery), "Permission(ManageOrders) Module(Food)" },
        { typeof(CreateDiningTableCommand), "Permission(ManageCatalog) Module(Food)" },
        { typeof(GetServiceBoardPiecesQuery), "Permission(ManageServiceBoard) Module(Service)" },
        // Charging and the drawer.
        { typeof(AddOrderPaymentCommand), "Permission(ChargeOrders)" },
        { typeof(UploadOrderPaymentEvidenceCommand), "Permission(ChargeOrders)" },
        // Private files open where they are seen: receipts with the payments, service photos with the service.
        { typeof(GetPaymentEvidenceFileQuery), "Permission(ManageOrders, ChargeOrders)" },
        { typeof(GetServicePhotoFileQuery), "Permission(ManageOrders, ManageServiceBoard)" },
        { typeof(OpenCashSessionCommand), "Permission(ManageCash)" },
        // Reports, and the business's activity for owners and admins.
        { typeof(GetDashboardMetricsQuery), "Permission(ViewReports)" },
        { typeof(GetSalesByBranchQuery), "Permission(ViewReports)" },
        { typeof(GetSalesByChannelQuery), "Permission(ViewReports)" },
        { typeof(GetDeliveryPlatformOrdersQuery), "Permission(ViewReports)" },
        { typeof(GetAuditLogsQuery), "Manager" },
        // Configuration stays with owners and admins; every member reads where they work.
        { typeof(UpdateMyTenantCommand), "Manager" },
        { typeof(UpdateSalesPolicyCommand), "Manager" },
        { typeof(UpdateBusinessThemeCommand), "Manager" },
        { typeof(UpdateOpeningHoursCommand), "Manager" },
        { typeof(GetSpecialDaysQuery), "Manager" },
        { typeof(AddSpecialDayCommand), "Manager" },
        { typeof(UpdateSpecialDayCommand), "Manager" },
        { typeof(RemoveSpecialDayCommand), "Manager" },
        // The rates of the delivery platforms are for owners and admins; whoever sells or reads the reports learns only
        // which platforms are on.
        { typeof(GetDeliveryPlatformsQuery), "Manager" },
        { typeof(SaveDeliveryPlatformCommand), "Manager" },
        { typeof(GetDeliveryPlatformChannelsQuery), "Permission(ManageOrders, ViewReports, ManageCatalog)" },
        // The prices of the products in each app, as the menu's: whoever manages the catalog.
        { typeof(GetPlatformPriceListQuery), "Permission(ManageCatalog)" },
        { typeof(SavePlatformPricesCommand), "Permission(ManageCatalog)" },
        // The settlements of their deposits: read with the reports, where the commission and the net already are; opened,
        // saved, closed and voided by owners and admins, as the terms.
        { typeof(GetPlatformSettlementsQuery), "Permission(ViewReports)" },
        { typeof(GetPlatformSettlementQuery), "Permission(ViewReports)" },
        { typeof(GetPendingPlatformOrdersQuery), "Permission(ViewReports)" },
        { typeof(GetPlatformReceivablesQuery), "Permission(ViewReports)" },
        { typeof(CreatePlatformSettlementCommand), "Manager" },
        { typeof(UpdatePlatformSettlementCommand), "Manager" },
        { typeof(ClosePlatformSettlementCommand), "Manager" },
        { typeof(VoidPlatformSettlementCommand), "Manager" },
        // The courier and the cash it collects (Fase 22D): whoever takes the orders sends one out with it; whoever handles the
        // cash reads what the couriers owe and settles it, charging the orders in their own drawer (ManageCash as well).
        { typeof(DispatchOrderCommand), "Permission(ManageOrders)" },
        { typeof(GetCourierReceivablesQuery), "Permission(ManageCash)" },
        { typeof(GetPendingCourierOrdersQuery), "Permission(ManageCash)" },
        { typeof(GetCourierSettlementsQuery), "Permission(ManageCash)" },
        { typeof(GetCourierSettlementQuery), "Permission(ManageCash)" },
        { typeof(CreateCourierSettlementCommand), "Permission(ChargeOrders)" },
        { typeof(InstallStarterCatalogCommand), "Manager" },
        { typeof(UploadTenantLogoCommand), "Manager" },
        { typeof(UpdateWhatsAppConfigCommand), "Manager" },
        { typeof(CreateBranchCommand), "Manager" },
        { typeof(GetBranchesQuery), "Member" },
        // Messages and customers.
        { typeof(SendChatMessageCommand), "Permission(ManageWhatsApp)" },
        { typeof(SendPromotionalMessageCommand), "Permission(ManageCampaigns)" },
        { typeof(CreateCampaignCommand), "Permission(ManageCampaigns)" },
        { typeof(SetMessagingOptOutCommand), "Permission(ManageCampaigns, ManageCustomers)" },
        { typeof(GetCustomerCardQuery), "Permission(ManageCustomers, ManageWhatsApp, ManageOrders)" },
        { typeof(CreateCustomerCommand), "Permission(ManageCustomers)" },
        { typeof(GetAiAgentsQuery), "Permission(ManageAiAgent)" },
        // The catalog is read by whoever sells, stocks or books it, and kept by whoever manages it.
        { typeof(GetProductsQuery), "Permission(ManageCatalog, ManageOrders, ManageInventory, ManageAppointments)" },
        { typeof(CreateProductCommand), "Permission(ManageCatalog)" }
    };

    [Theory]
    [MemberData(nameof(Jobs))]
    public void Job_ShouldDeclareWhoMayDoIt(Type request, string expected)
    {
        Assert.Equal(expected, Describe(request));
    }

    [Fact]
    public void CourierSettlement_ShouldAlsoNeedTheDrawer_SinceTheCashEntersIt()
    {
        var settlement = new CreateCourierSettlementCommand(
            DeliveryCourier.Paquetenvia, OrderPaymentMethod.Cash, 0m, [], [], null, null);

        Assert.Equal([TenantPermission.ManageCash], settlement.RequiredPermissions());
    }

    [Fact]
    public void TheTeam_ShouldBeManagedByOwnersAndAdminsOnly()
    {
        // Whoever manages the permissions could give themselves all of them.
        var requests = RequestsIn("FoodParkDigital.Application.Employees");

        var others = requests.Where(t => Describe(t) != "Manager").Select(t => t.Name).ToList();

        Assert.NotEmpty(requests);
        Assert.True(others.Count == 0, "Team requests open to staff: " + string.Join(", ", others));
    }

    [Theory]
    [InlineData("FoodParkDigital.Application.Inventory", TenantCapability.Inventory)]
    [InlineData("FoodParkDigital.Application.Purchasing", TenantCapability.Inventory)]
    [InlineData("FoodParkDigital.Application.Appointments", TenantCapability.Appointments)]
    [InlineData("FoodParkDigital.Application.Dining", TenantCapability.Food)]
    public void ModuleScreens_ShouldNeedTheirModule(string feature, TenantCapability module)
    {
        var requests = RequestsIn(feature);

        var without = requests
            .Where(t => RequestAuthorizationRules.For(t).Capability?.AnyOf != module)
            .Select(t => t.Name)
            .ToList();

        Assert.NotEmpty(requests);
        Assert.True(without.Count == 0, $"Requests without the {module} module: " + string.Join(", ", without));
    }

    private static string Describe(Type request)
    {
        var rule = RequestAuthorizationRules.For(request);
        var who = rule.Primary switch
        {
            RequiresPermissionAttribute permission => $"Permission({string.Join(", ", permission.AnyOf)})",
            RequiresTenantManagerAttribute => "Manager",
            RequiresTenantMemberAttribute => "Member",
            RequiresAuthenticationAttribute => "Authenticated",
            RequiresPlatformAdminAttribute => "PlatformAdmin",
            AllowAnonymousRequestAttribute => "Anonymous",
            _ => "None"
        };
        return rule.Capability is { } module ? $"{who} Module({module.AnyOf})" : who;
    }

    private static List<Type> RequestsIn(string feature)
        => typeof(Result).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && (t.Namespace == feature || t.Namespace?.StartsWith(feature + ".", StringComparison.Ordinal) == true)
                && t.GetInterfaces().Any(i => i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
}
