using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

/// <summary>Businesses as the API returns them; without settings loaded, the defaults of a new business apply.</summary>
public static class TenantMappings
{
    /// <summary>"Mi negocio": the profile, the sales and stock policies and the fiscal address.</summary>
    public static TenantProfileDto ToProfileDto(this Tenant tenant) => new()
    {
        Id = tenant.Id,
        Name = tenant.Name,
        Slug = tenant.Slug,
        BusinessType = tenant.BusinessType,
        Status = tenant.Status,
        ContactEmail = tenant.ContactEmail,
        ContactPhone = tenant.ContactPhone,
        LogoUrl = string.IsNullOrEmpty(tenant.LogoUrl) ? null : $"{tenant.LogoUrl}_md.webp",
        LogoImages = LogoImageUrlsDto.FromBaseUrl(tenant.LogoUrl),
        Capabilities = tenant.Settings?.Capabilities ?? TenantCapability.None,
        CreatedAt = tenant.CreatedAt,
        PricesIncludeTax = tenant.Settings?.PricesIncludeTax ?? false,
        DefaultTaxRate = tenant.Settings?.DefaultTaxRate ?? VatRate.GeneralPercent,
        AutoCloseCounterSales = tenant.Settings?.AutoCloseCounterSales ?? true,
        OrderNotificationPolicy = tenant.Settings?.OrderNotificationPolicy ?? OrderNotificationPolicy.RemoteAndServices,
        InsufficientStockPolicy = tenant.Settings?.InsufficientStockPolicy ?? InsufficientStockPolicy.Block,
        OnlineMenuMode = tenant.Settings?.OnlineMenuMode ?? OnlineMenuMode.Off,
        Theme = tenant.Settings?.Theme ?? BusinessTheme.Nopal,
        OnboardingCompletedAt = tenant.Settings?.OnboardingCompletedAt,
        TimeZone = tenant.Settings?.TimeZone ?? BusinessTimeZones.Default,
        OpeningHours = tenant.Settings?.OpeningHours?.ToDtos(),
        Rfc = tenant.Rfc,
        LegalName = tenant.LegalName,
        Industry = tenant.Industry,
        TaxRegime = tenant.TaxRegime,
        Street = tenant.Address?.Street,
        ExteriorNumber = tenant.Address?.ExteriorNumber,
        InteriorNumber = tenant.Address?.InteriorNumber,
        Neighborhood = tenant.Address?.Neighborhood,
        ZipCode = tenant.Address?.ZipCode,
        City = tenant.Address?.City,
        State = tenant.Address?.State,
        Country = tenant.Address?.Country
    };

    /// <summary>A row of the platform's businesses list (SQL).</summary>
    public static readonly Expression<Func<Tenant, TenantDto>> AsDto = tenant => new TenantDto
    {
        Id = tenant.Id,
        Name = tenant.Name,
        Slug = tenant.Slug,
        BusinessType = tenant.BusinessType,
        Status = tenant.Status,
        ContactEmail = tenant.ContactEmail,
        ContactPhone = tenant.ContactPhone,
        LogoUrl = tenant.LogoUrl,
        Capabilities = tenant.Settings != null ? tenant.Settings.Capabilities : TenantCapability.None,
        CreatedAt = tenant.CreatedAt
    };
}
