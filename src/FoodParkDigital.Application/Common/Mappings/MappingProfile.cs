using AutoMapper;
using FoodParkDigital.Application.Audit.DTOs;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Finance.DTOs;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Purchasing.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.WhatsApp.Entities;

namespace FoodParkDigital.Application.Common.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Order, OrderDto>()
            .ForMember(d => d.TaxBreakdown, opt => opt.Ignore())
            .ForMember(d => d.NetPaidAmount, opt => opt.MapFrom(s => s.PaidAmount - s.RefundedAmount));
        CreateMap<OrderItem, OrderItemDto>();
        CreateMap<OrderStatusHistory, OrderStatusHistoryDto>();
        CreateMap<ServiceOrderDetail, ServiceOrderDetailDto>();
        CreateMap<ServiceOrderPiece, ServiceOrderPieceDto>();
        CreateMap<OrderPayment, OrderPaymentDto>();
        CreateMap<OrderRefund, OrderRefundDto>();
        CreateMap<OrderRefundLine, OrderRefundLineDto>();
        CreateMap<Order, KitchenOrderDto>();

        CreateMap<Product, ProductDto>()
            .ForMember(d => d.ImageUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.ImageUrl) ? null : $"{s.ImageUrl}_md.webp"))
            .ForMember(d => d.Images, opt => opt.MapFrom(s =>
                ProductImageUrlsDto.FromBaseUrl(s.ImageUrl)));
        CreateMap<Category, CategoryDto>();
        CreateMap<Customer, CustomerDto>();

        CreateMap<Tenant, TenantProfileDto>()
            .ForMember(d => d.LogoUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.LogoUrl) ? null : $"{s.LogoUrl}_md.webp"))
            .ForMember(d => d.LogoImages, opt => opt.MapFrom(s =>
                LogoImageUrlsDto.FromBaseUrl(s.LogoUrl)))
            .ForMember(d => d.Capabilities, opt => opt.MapFrom(s =>
                s.Settings != null ? s.Settings.Capabilities : 0))
            .ForMember(d => d.PricesIncludeTax, opt => opt.MapFrom(s =>
                s.Settings != null && s.Settings.PricesIncludeTax))
            .ForMember(d => d.DefaultTaxRate, opt => opt.MapFrom(s =>
                s.Settings != null ? s.Settings.DefaultTaxRate : VatRate.GeneralPercent))
            .ForMember(d => d.AutoCloseCounterSales, opt => opt.MapFrom(s =>
                s.Settings == null || s.Settings.AutoCloseCounterSales))
            .ForMember(d => d.RequireOpenCashSession, opt => opt.MapFrom(s =>
                s.Settings != null && s.Settings.RequireOpenCashSession))
            .ForMember(d => d.OnboardingCompletedAt, opt => opt.MapFrom(s =>
                s.Settings != null ? s.Settings.OnboardingCompletedAt : null))
            .ForMember(d => d.Street, opt => opt.MapFrom(s => s.Address != null ? s.Address.Street : null))
            .ForMember(d => d.ExteriorNumber, opt => opt.MapFrom(s => s.Address != null ? s.Address.ExteriorNumber : null))
            .ForMember(d => d.InteriorNumber, opt => opt.MapFrom(s => s.Address != null ? s.Address.InteriorNumber : null))
            .ForMember(d => d.Neighborhood, opt => opt.MapFrom(s => s.Address != null ? s.Address.Neighborhood : null))
            .ForMember(d => d.ZipCode, opt => opt.MapFrom(s => s.Address != null ? s.Address.ZipCode : null))
            .ForMember(d => d.City, opt => opt.MapFrom(s => s.Address != null ? s.Address.City : null))
            .ForMember(d => d.State, opt => opt.MapFrom(s => s.Address != null ? s.Address.State : null))
            .ForMember(d => d.Country, opt => opt.MapFrom(s => s.Address != null ? s.Address.Country : null));

        CreateMap<Tenant, TenantDto>()
            .ForMember(d => d.Capabilities, opt => opt.MapFrom(s =>
                s.Settings != null ? s.Settings.Capabilities : 0));

        CreateMap<ApplicationUser, UserDto>()
            .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role.ToString()))
            .ForMember(d => d.TenantId, opt => opt.MapFrom(s => s.DefaultTenantId))
            .ForMember(d => d.AvatarUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.AvatarUrl) ? null : $"{s.AvatarUrl}_md.webp"))
            .ForMember(d => d.AvatarImages, opt => opt.MapFrom(s =>
                AvatarImageUrlsDto.FromBaseUrl(s.AvatarUrl)));

        CreateMap<ApplicationUser, UserProfileDto>()
            .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role.ToString()))
            .ForMember(d => d.TenantId, opt => opt.MapFrom(s => s.DefaultTenantId))
            .ForMember(d => d.AvatarUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.AvatarUrl) ? null : $"{s.AvatarUrl}_md.webp"))
            .ForMember(d => d.AvatarImages, opt => opt.MapFrom(s =>
                AvatarImageUrlsDto.FromBaseUrl(s.AvatarUrl)));

        CreateMap<AuditLog, AuditLogDto>();
        CreateMap<AiAgent, AiAgentDto>();
        CreateMap<SupplyItem, SupplyItemDto>();
        CreateMap<InventoryLocation, InventoryLocationDto>();
        CreateMap<InventoryMovement, InventoryMovementDto>();
        CreateMap<InventoryMovementLine, InventoryMovementLineDto>();
        CreateMap<Supplier, SupplierDto>();
        CreateMap<PurchaseOrder, PurchaseOrderDto>();
        CreateMap<PurchaseOrderLine, PurchaseOrderLineDto>();
        CreateMap<CashSession, CashSessionDto>();
        CreateMap<CashTransaction, CashTransactionDto>();
        CreateMap<Appointment, AppointmentDto>();

        CreateMap<WhatsAppConversation, ConversationDto>();
        CreateMap<WhatsAppMessage, ChatMessageDto>()
            .ForMember(d => d.Direction, opt => opt.MapFrom(s => s.Direction.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
