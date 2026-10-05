using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Services;

public sealed class AppLinks : IAppLinks
{
    public AppLinks(IConfiguration configuration)
    {
        var appUrl = configuration["AppUrl"]?.Trim().TrimEnd('/');
        BaseUrl = string.IsNullOrEmpty(appUrl) ? null : appUrl;
    }

    public string? BaseUrl { get; }
}
