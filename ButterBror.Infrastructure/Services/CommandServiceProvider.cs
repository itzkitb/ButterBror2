using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Infrastructure.Services;

public class CommandServiceProvider(IServiceProvider serviceProvider) : ICommandServiceProvider
{
    private readonly IDynamicServiceProvider? _dynamicProvider = serviceProvider.GetService<IDynamicServiceProvider>();

    public T GetService<T>() where T : notnull
    {
        var service = _dynamicProvider?.GetService(typeof(T)) ?? serviceProvider.GetService(typeof(T));
        
        return service is T typedService 
            ? typedService 
            : throw new InvalidOperationException($"service of type {typeof(T).Name} is not registered");
    }

    public T? GetService<T>(string? key = null) where T : notnull
    {
        if (key == null)
        {
            return serviceProvider.GetService<T>();
        }
        
        var services = _dynamicProvider != null 
            ? _dynamicProvider.GetServices<T>() 
            : serviceProvider.GetServices<T>();

        var namedService = services.FirstOrDefault(s => 
            s.GetType().Name.Contains(key, StringComparison.OrdinalIgnoreCase));

        return namedService ?? GetService<T>();
    }
}
