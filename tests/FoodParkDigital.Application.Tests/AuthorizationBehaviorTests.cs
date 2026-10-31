using System.Reflection;
using FoodParkDigital.Application.Common.Behaviors;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Tests;

/// <summary>Authorization v2: every request declares who may send it and the pipeline denies the rest.</summary>
public class AuthorizationBehaviorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void EveryRequest_ShouldDeclareExactlyOneAuthorizationRule()
    {
        var requests = typeof(AuthorizationBehavior<,>).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && t.GetInterfaces().Any(i => i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))))
            .ToList();

        var withoutSingleRule = requests
            .Where(t => t.GetCustomAttributes<RequestAuthorizationAttribute>(inherit: false).Count() != 1)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(requests.Count > 100, $"Only {requests.Count} requests found.");
        Assert.True(withoutSingleRule.Count == 0, "Requests without exactly one rule: " + string.Join(", ", withoutSingleRule));
    }

    [Fact]
    public void AnonymousRequests_ShouldBeOnlyThePublicAndSystemFlows()
    {
        var anonymous = typeof(AuthorizationBehavior<,>).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<AllowAnonymousRequestAttribute>(inherit: false) is not null)
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(
        [
            "AcceptInvitationCommand", "ApplyOnlinePaymentCommand", "ApplyOnlinePaymentNotificationCommand", "ApplyWhatsAppStatusesCommand", "CancelPublicAppointmentCommand",
            "ChangeUnconfirmedPhoneCommand", "CheckInvoiceCancellationsCommand", "ConfirmEmailCommand", "ConfirmOnlineCheckoutCommand", "ConfirmPhoneCommand",
            "CreateOnlineCheckoutCommand",
            "ForgotPasswordBySmsCommand", "ForgotPasswordCommand", "GetInvitationQuery", "GetPublicAppointmentQuery", "GetPublicBookingQuery",
            "GetPublicBookingSlotsQuery", "GetPublicCfdiFileQuery", "GetPublicCfdiQuery", "GetPublicDeliveryQuoteQuery",
            "GetPublicDirectoryQuery",
            "GetPublicMenuQuery", "GetPublicOrderTrackingQuery", "GetPublicReviewsQuery", "GetPublicSitemapQuery", "GetSelfInvoicePageQuery",
            "GetStorePreviewQuery",
            "IssuePendingCreditNotesCommand", "IssuePendingPaymentComplementsCommand", "Login2FACommand", "LoginCommand", "LogoutCommand", "LookUpSelfInvoiceTicketQuery",
            "PlacePublicOrderCommand",
            "ProcessInboundMessageCommand", "QueueMonthlyAccountantEmailCommand", "ReconcileOnlinePaymentCommand", "RefreshTokenCommand",
            "RegisterBusinessCommand", "RegisterCustomerCommand", "RequestAutomaticCreditNoteCommand", "RequestAutomaticPaymentComplementCommand",
            "RequestPublicAppointmentCommand", "RequestSelfInvoiceCommand", "ResendConfirmationEmailCommand",
            "ResendPhoneConfirmationCommand", "ResendSelfInvoiceCommand", "ResetPasswordCommand", "ResetPasswordWithSmsCodeCommand",
            "ReviewOnlinePaymentsCommand", "SelfInvoiceCommand",
            "StampPendingInvoicesCommand", "SweepOnlinePaymentsCommand",
            "VerifyWhatsAppWebhookTokenQuery"
        ], anonymous);
    }

    [Fact]
    public async Task RequestWithoutRule_ShouldBeDenied()
    {
        var behavior = Behavior<Unruled>(Staff(permissions: ["ManageOrders"]));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => behavior.Handle(new Unruled(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task Permission_ShouldBeRequiredFromStaffButNotFromOwners()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<NeedsCash>(Staff(permissions: ["ManageOrders"])).Handle(new NeedsCash(), Next, CancellationToken.None));

        Assert.Equal("ok", await Behavior<NeedsCash>(Staff(permissions: ["managecash"])).Handle(new NeedsCash(), Next, CancellationToken.None));
        Assert.Equal("ok", await Behavior<NeedsCash>(Staff(permissions: [], tenantRole: "OWNER")).Handle(new NeedsCash(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task Waiter_ShouldTakeOrdersButNotCharge()
    {
        var waiter = Staff(permissions: ["ManageOrders"]);
        var cashier = Staff(permissions: ["ManageOrders", "ChargeOrders"]);
        var owner = Staff(permissions: [], tenantRole: "OWNER");

        Assert.Equal("ok", await Behavior<SaleShape>(waiter).Handle(new SaleShape(Charged: false), Next, CancellationToken.None));
        var refused = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<SaleShape>(waiter).Handle(new SaleShape(Charged: true), Next, CancellationToken.None));
        Assert.Equal(AuthorizationBehavior<SaleShape, string>.ChargeNotAllowedMessage, refused.Message);
        Assert.Equal("ok", await Behavior<SaleShape>(cashier).Handle(new SaleShape(Charged: true), Next, CancellationToken.None));
        Assert.Equal("ok", await Behavior<SaleShape>(owner).Handle(new SaleShape(Charged: true), Next, CancellationToken.None));
    }

    [Fact]
    public void Sales_ShouldNeedTheChargePermissionOnlyWithPayments()
    {
        Assert.Empty(new Orders.Commands.CreateOrderCommand().RequiredPermissions());
        Assert.Equal(
            [TenantPermission.ChargeOrders],
            new Orders.Commands.CreateOrderCommand { Payments = [new() { Amount = 10m }] }.RequiredPermissions());
        // The sale of an appointment completes it in the agenda (Fase 22E): whoever books appointments charges it.
        Assert.Equal(
            [TenantPermission.ManageAppointments],
            new Orders.Commands.CreateOrderCommand { AppointmentId = Guid.NewGuid() }.RequiredPermissions());
        Assert.Equal(
            TenantPermission.ChargeOrders,
            Assert.Single(typeof(Orders.Commands.AddOrderPaymentCommand).GetCustomAttribute<RequiresPermissionAttribute>()!.AnyOf));
        Assert.Equal(
            TenantPermission.ChargeOrders,
            Assert.Single(typeof(Orders.Commands.UploadOrderPaymentEvidenceCommand).GetCustomAttribute<RequiresPermissionAttribute>()!.AnyOf));
    }

    [Fact]
    public async Task Refunds_ShouldNeedTheRefundPermission_BesidesTakingOrders()
    {
        var waiter = Staff(permissions: ["ManageOrders"]);
        var supervisor = Staff(permissions: ["ManageOrders", "ProcessRefunds"]);
        var owner = Staff(permissions: [], tenantRole: "OWNER");

        var refused = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<Orders.Commands.RefundOrderCommand>(waiter).Handle(new Orders.Commands.RefundOrderCommand(), Next, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<Orders.Commands.VoidOrderCommand>(waiter).Handle(new Orders.Commands.VoidOrderCommand(), Next, CancellationToken.None));

        Assert.Equal(AuthorizationBehavior<Orders.Commands.RefundOrderCommand, string>.RefundNotAllowedMessage, refused.Message);
        Assert.Equal("ok", await Behavior<Orders.Commands.RefundOrderCommand>(supervisor).Handle(new Orders.Commands.RefundOrderCommand(), Next, CancellationToken.None));
        Assert.Equal("ok", await Behavior<Orders.Commands.VoidOrderCommand>(owner).Handle(new Orders.Commands.VoidOrderCommand(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task InactiveMember_ShouldBeDeniedEvenWithThePermission()
    {
        var access = Staff(permissions: ["ManageCash"], active: false);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<NeedsCash>(access).Handle(new NeedsCash(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task Capability_ShouldApplyToEveryoneIncludingThePlatformAdmin()
    {
        var withoutKitchen = Staff(permissions: [], role: CurrentAccessContext.PlatformAdminRole, capabilities: TenantCapability.Retail);
        var withKitchen = Staff(permissions: ["ManageServiceBoard"], capabilities: TenantCapability.Food | TenantCapability.Kitchen);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<NeedsKitchen>(withoutKitchen).Handle(new NeedsKitchen(), Next, CancellationToken.None));
        Assert.Equal("ok", await Behavior<NeedsKitchen>(withKitchen).Handle(new NeedsKitchen(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task Anonymous_ShouldPassWithoutUserAndAuthenticated_ShouldNeedOne()
    {
        var nobody = new FakeAccess { UserId = null };

        Assert.Equal("ok", await Behavior<Public>(nobody).Handle(new Public(), Next, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Behavior<GetMyAccessQueryShape>(nobody).Handle(new GetMyAccessQueryShape(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task TenantManager_ShouldRejectStaffAndAcceptAdmins()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<ManagersOnly>(Staff(permissions: ["ManageCash", "ManageEmployees"])).Handle(new ManagersOnly(), Next, CancellationToken.None));
        Assert.Equal("ok", await Behavior<ManagersOnly>(Staff(permissions: [], role: "TENANT_ADMIN", tenantRole: "ADMIN")).Handle(new ManagersOnly(), Next, CancellationToken.None));
    }

    [Fact]
    public async Task OwningAnotherBusiness_ShouldGiveNoAdminRightsWhereThePersonIsStaff()
    {
        // A TENANT_ADMIN account (it owns another business) that is staff here, or an admin demoted after the token was issued.
        var staffHere = Staff(permissions: ["ManageOrders"], role: "TENANT_ADMIN", tenantRole: "STAFF");

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<ManagersOnly>(staffHere).Handle(new ManagersOnly(), Next, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            Behavior<NeedsCash>(staffHere).Handle(new NeedsCash(), Next, CancellationToken.None));
        var access = await new GetMyAccessQueryHandler(staffHere.Context, staffHere.User).Handle(new GetMyAccessQuery(), CancellationToken.None);
        Assert.False(access.IsTenantManager);
        Assert.Equal(["ManageOrders"], access.Permissions);
        // The menu follows the role in this business, not the one an older token carries.
        Assert.Equal("STAFF", access.Role);
    }

    [Fact]
    public async Task MyAccess_ShouldResolveEffectivePermissionsFromTheServer()
    {
        var staff = Staff(permissions: ["ManageOrders", "ManageCash"], capabilities: TenantCapability.Retail | TenantCapability.Inventory);
        var owner = Staff(permissions: [], tenantRole: "OWNER");

        var staffAccess = await new GetMyAccessQueryHandler(staff.Context, staff.User).Handle(new GetMyAccessQuery(), CancellationToken.None);
        var ownerAccess = await new GetMyAccessQueryHandler(owner.Context, owner.User).Handle(new GetMyAccessQuery(), CancellationToken.None);

        Assert.Equal(["ManageCash", "ManageOrders"], staffAccess.Permissions);
        Assert.False(staffAccess.IsTenantManager);
        Assert.Equal((int)(TenantCapability.Retail | TenantCapability.Inventory), staffAccess.Capabilities);
        Assert.True(ownerAccess.IsTenantManager);
        Assert.Equal(Enum.GetNames<TenantPermission>().Length, ownerAccess.Permissions.Count);
        Assert.Equal("STAFF", staffAccess.Role);
        Assert.Equal("TENANT_ADMIN", ownerAccess.Role);
    }

    private static Task<string> Next() => Task.FromResult("ok");

    private static AuthorizationBehavior<TRequest, string> Behavior<TRequest>(FakeAccess access)
        where TRequest : notnull
        => new(access.Context);

    private static FakeAccess Staff(
        IReadOnlyCollection<string> permissions,
        string role = "STAFF",
        string tenantRole = "STAFF",
        bool active = true,
        TenantCapability capabilities = TenantCapability.Retail | TenantCapability.Kitchen)
        => new()
        {
            Role = role,
            TenantRole = tenantRole,
            Snapshot = new TenantAccessSnapshot(
                active,
                capabilities,
                permissions,
                IsManager: active && tenantRole is "OWNER" or "ADMIN",
                EffectiveRole: active
                    ? TenantPermissionCatalog.EffectiveRole(AccountRole(role), Enum.Parse<TenantUserRole>(tenantRole, ignoreCase: true))
                    : null),
            Capabilities = capabilities
        };

    private static UserRole AccountRole(string role) => role switch
    {
        "SUPER_ADMIN" => UserRole.SuperAdmin,
        "TENANT_ADMIN" => UserRole.TenantAdmin,
        "KITCHEN" => UserRole.Kitchen,
        _ => UserRole.Staff
    };

    private sealed record Unruled : IRequest<string>;

    [RequiresPermission(TenantPermission.ManageCash)]
    private sealed record NeedsCash : IRequest<string>;

    [RequiresPermission(TenantPermission.ManageServiceBoard)]
    [RequiresCapability(TenantCapability.Kitchen)]
    private sealed record NeedsKitchen : IRequest<string>;

    [AllowAnonymousRequest("test")]
    private sealed record Public : IRequest<string>;

    [RequiresAuthentication]
    private sealed record GetMyAccessQueryShape : IRequest<string>;

    [RequiresTenantManager]
    private sealed record ManagersOnly : IRequest<string>;

    /// <summary>Like a sale: taking it needs ManageOrders, charging it in the same request needs ChargeOrders too.</summary>
    [RequiresPermission(TenantPermission.ManageOrders)]
    private sealed record SaleShape(bool Charged) : IRequest<string>, IRequiresContentPermissions
    {
        public IEnumerable<TenantPermission> RequiredPermissions() => Charged ? [TenantPermission.ChargeOrders] : [];
    }

    private sealed class FakeAccess : ICurrentUserService, ICurrentTenantService, ITenantAccessService
    {
        public string? UserId { get; init; } = Guid.NewGuid().ToString();
        public string? UserName => "test";
        public string? Role { get; init; }
        public string? TenantRole { get; init; }
        public IReadOnlyCollection<string> Permissions => [];
        Guid? ICurrentUserService.TenantId => TenantId;
        public Guid TenantId => AuthorizationBehaviorTests.TenantId;
        public TenantAccessSnapshot Snapshot { get; init; } = new(false, TenantCapability.None, []);
        public TenantCapability Capabilities { get; init; }

        public ICurrentUserService User => this;
        public CurrentAccessContext Context => new(this, this, this);

        public void SetTenant(Guid tenantId) { }
        public Task<TenantAccessSnapshot> GetAccessSnapshotAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
        public Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Snapshot.HasActiveAccess);
        public Task<bool> HasCapabilityAsync(Guid tenantId, TenantCapability capability, CancellationToken cancellationToken) => Task.FromResult((Capabilities & capability) == capability);
        public Task<bool> HasAnyCapabilityAsync(Guid tenantId, TenantCapability capabilities, CancellationToken cancellationToken) => Task.FromResult((Capabilities & capabilities) != 0);
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult(Snapshot.Permissions);
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, TenantPermission permission, CancellationToken cancellationToken) => Task.FromResult(Snapshot.Permissions.Contains(permission.ToString()));
    }
}
