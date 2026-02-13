using AutoMapper;
using FoodParkDigital.Application.Audit.DTOs;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;

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

        CreateMap<ApplicationUser, UserDto>()
            .ForMember(d => d.Role, opt => opt.MapFrom(s => s.Role.ToString()));

        CreateMap<AuditLog, AuditLogDto>();
    }
}
