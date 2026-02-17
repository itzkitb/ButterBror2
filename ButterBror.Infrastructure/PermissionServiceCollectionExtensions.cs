using ButterBror.Core.Interfaces;
using ButterBror.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Infrastructure;

public static class PermissionServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionManager(this IServiceCollection services)
    {
        services.AddScoped<IPermissionManager, PermissionManager>();
        return services;
    }
}
