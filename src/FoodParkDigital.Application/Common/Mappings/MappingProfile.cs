using AutoMapper;
using FoodParkDigital.Application.Audit.DTOs;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.WhatsApp.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.WhatsApp.Entities;

namespace FoodParkDigital.Application.Common.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Order, OrderDto>();
        CreateMap<OrderItem, OrderItemDto>();
        CreateMap<OrderStatusHistory, OrderStatusHistoryDto>();
        CreateMap<Order, KitchenOrderDto>();

        CreateMap<Product, ProductDto>()
            .ForMember(d => d.ImageUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.ImageUrl) ? null : $"{s.ImageUrl}_md.webp"))
            .ForMember(d => d.Images, opt => opt.MapFrom(s =>
                ProductImageUrlsDto.FromBaseUrl(s.ImageUrl)));
        CreateMap<Category, CategoryDto>();

        CreateMap<Tenant, TenantDto>();
        CreateMap<Tenant, TenantProfileDto>()
            .ForMember(d => d.LogoUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.LogoUrl) ? null : $"{s.LogoUrl}_md.webp"))
            .ForMember(d => d.LogoImages, opt => opt.MapFrom(s =>
                LogoImageUrlsDto.FromBaseUrl(s.LogoUrl)))
            .ForMember(d => d.Street, opt => opt.MapFrom(s => s.Address != null ? s.Address.Street : null))
            .ForMember(d => d.ExteriorNumber, opt => opt.MapFrom(s => s.Address != null ? s.Address.ExteriorNumber : null))
            .ForMember(d => d.InteriorNumber, opt => opt.MapFrom(s => s.Address != null ? s.Address.InteriorNumber : null))
            .ForMember(d => d.Neighborhood, opt => opt.MapFrom(s => s.Address != null ? s.Address.Neighborhood : null))
            .ForMember(d => d.ZipCode, opt => opt.MapFrom(s => s.Address != null ? s.Address.ZipCode : null))
            .ForMember(d => d.City, opt => opt.MapFrom(s => s.Address != null ? s.Address.City : null))
            .ForMember(d => d.State, opt => opt.MapFrom(s => s.Address != null ? s.Address.State : null))
            .ForMember(d => d.Country, opt => opt.MapFrom(s => s.Address != null ? s.Address.Country : null));

        CreateMap<ApplicationUser, UserDto>()
            .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role.ToString()))
            .ForMember(d => d.AvatarUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.AvatarUrl) ? null : $"{s.AvatarUrl}_md.webp"))
            .ForMember(d => d.AvatarImages, opt => opt.MapFrom(s =>
                AvatarImageUrlsDto.FromBaseUrl(s.AvatarUrl)));

        CreateMap<ApplicationUser, UserProfileDto>()
            .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role.ToString()))
            .ForMember(d => d.AvatarUrl, opt => opt.MapFrom(s =>
                string.IsNullOrEmpty(s.AvatarUrl) ? null : $"{s.AvatarUrl}_md.webp"))
            .ForMember(d => d.AvatarImages, opt => opt.MapFrom(s =>
                AvatarImageUrlsDto.FromBaseUrl(s.AvatarUrl)));

        CreateMap<AuditLog, AuditLogDto>();

        CreateMap<WhatsAppConversation, ConversationDto>();
        CreateMap<WhatsAppMessage, ChatMessageDto>()
            .ForMember(d => d.Direction, opt => opt.MapFrom(s => s.Direction.ToString()))
            .ForMember(d => d.Status, opt => opt.MapFrom(s => s.Status.ToString()));
    }
}
