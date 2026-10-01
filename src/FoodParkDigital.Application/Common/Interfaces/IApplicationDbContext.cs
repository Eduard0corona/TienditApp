using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.WhatsApp.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantUser> TenantUsers { get; }
    DbSet<TenantSettings> TenantSettings { get; }
    DbSet<WhatsAppConfig> WhatsAppConfigs { get; }
    DbSet<WhatsAppConversation> WhatsAppConversations { get; }
    DbSet<WhatsAppMessage> WhatsAppMessages { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<TemporaryCustomer> TemporaryCustomers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderStatusHistory> OrderStatusHistories { get; }
    DbSet<OrderPayment> OrderPayments { get; }
    DbSet<ServiceOrderDetail> ServiceOrderDetails { get; }
    DbSet<ServiceOrderPiece> ServiceOrderPieces { get; }
    DbSet<Appointment> Appointments { get; }
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
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
