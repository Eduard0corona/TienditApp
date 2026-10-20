using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Billing.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Reporting.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<SpecialDay> SpecialDays { get; }
    DbSet<WhatsAppConfig> WhatsAppConfigs { get; }
    DbSet<WhatsAppConversation> WhatsAppConversations { get; }
    DbSet<WhatsAppMessage> WhatsAppMessages { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<CustomerBalanceEntry> CustomerBalanceEntries { get; }
    DbSet<AccountAddress> AccountAddresses { get; }
    DbSet<FavoriteBusiness> FavoriteBusinesses { get; }
    DbSet<BusinessReview> BusinessReviews { get; }
    DbSet<OnlinePaymentAccount> OnlinePaymentAccounts { get; }
    DbSet<OnlinePaymentAuthorization> OnlinePaymentAuthorizations { get; }
    DbSet<OnlineCheckout> OnlineCheckouts { get; }
    DbSet<OnlinePaymentRefund> OnlinePaymentRefunds { get; }
    DbSet<TemporaryCustomer> TemporaryCustomers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<OrderPayment> OrderPayments { get; }
    DbSet<OrderRefund> OrderRefunds { get; }
    DbSet<OrderWriteOff> OrderWriteOffs { get; }
    DbSet<ServiceOrderDetail> ServiceOrderDetails { get; }
    DbSet<PlatformOrderDetail> PlatformOrderDetails { get; }
    DbSet<OrderDelivery> OrderDeliveries { get; }
    DbSet<OrderVisitCharge> OrderVisitCharges { get; }
    DbSet<DeliveryPlatformSetting> DeliveryPlatformSettings { get; }
    DbSet<PlatformSettlement> PlatformSettlements { get; }
    DbSet<PlatformSettlementOrder> PlatformSettlementOrders { get; }
    DbSet<PlatformSettlementAdjustment> PlatformSettlementAdjustments { get; }
    DbSet<CourierSettlement> CourierSettlements { get; }
    DbSet<CourierSettlementOrder> CourierSettlementOrders { get; }
    DbSet<CourierSettlementAdjustment> CourierSettlementAdjustments { get; }
    DbSet<DeliveryZone> DeliveryZones { get; }
    DbSet<DeliveryZonePostalCode> DeliveryZonePostalCodes { get; }
    DbSet<ServiceOrderPiece> ServiceOrderPieces { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<OnlineBookingStaff> OnlineBookingStaff { get; }
    DbSet<AppointmentVisit> AppointmentVisits { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<CashSession> CashSessions { get; }
    DbSet<CashTransaction> CashTransactions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AiAgent> AiAgents { get; }
    DbSet<SupplyItem> SupplyItems { get; }
    DbSet<InventoryLocation> InventoryLocations { get; }
    DbSet<InventoryStock> InventoryStocks { get; }
    DbSet<ProductSupplyRequirement> ProductSupplyRequirements { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<InventoryMovementLine> InventoryMovementLines { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<OwnerWhatsAppConfig> OwnerWhatsAppConfigs { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<MessageCampaign> MessageCampaigns { get; }
    DbSet<OutgoingMessage> OutgoingMessages { get; }
    DbSet<MessagingOptOut> MessagingOptOuts { get; }
    DbSet<ProductOptionGroup> ProductOptionGroups { get; }
    DbSet<ProductOption> ProductOptions { get; }
    DbSet<ProductPlatformPrice> ProductPlatformPrices { get; }
    DbSet<ProductOptionPlatformPrice> ProductOptionPlatformPrices { get; }
    DbSet<OrderItemOption> OrderItemOptions { get; }
    DbSet<DiningTable> DiningTables { get; }
    DbSet<KitchenTicket> KitchenTickets { get; }
    DbSet<OrderItemCancellation> OrderItemCancellations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<DailySalesSnapshot> DailySalesSnapshots { get; }
    DbSet<Plan> Plans { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<SubscriptionPayment> SubscriptionPayments { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the realtime events of what was saved inside <paramref name="transaction"/>, once it committed. Inside a
    /// transaction they wait, so a screen never reloads before the data is there; a rolled-back one never publishes them.
    /// </summary>
    Task PublishCommittedEventsAsync(IDbContextTransaction transaction, CancellationToken cancellationToken);
}
